using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MeridianStudio.API.Infrastructure.WebSearch;

/// <summary>
/// GitHub Search API — free, no auth required (60 req/hr unauthenticated).
/// Used for IT Services, Telecommunications, and Manufacturing domains to surface
/// trending open-source AI projects as evidence for AI Fitness and Feasibility scoring.
/// </summary>
public sealed class GitHubTrendingProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<GitHubTrendingProvider> logger) : IWebSearchProvider
{
    private const string Endpoint = "https://api.github.com/search/repositories";

    public string Name => "GitHub";
    public bool IsConfigured => true;  // free, no key needed

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, CancellationToken ct = default)
    {
        var maxResults = config.GetValue<int>("WebSearch:MaxResultsPerQuery", 5);

        // Build query: add "AI" + filter to repos created/pushed recently
        var since      = DateTime.UtcNow.AddDays(-365).ToString("yyyy-MM-dd");
        var ghQuery    = $"{query} AI pushed:>{since}";
        var url        = $"{Endpoint}?q={Uri.EscapeDataString(ghQuery)}&sort=stars&order=desc&per_page={maxResults}";

        try
        {
            var client = httpClientFactory.CreateClient("GitHub");
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "MeridianStudio/1.0");
            req.Headers.Add("Accept", "application/vnd.github.v3+json");

            var response = await client.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var results = new List<SearchResult>();
            if (!doc.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var repo in items.EnumerateArray())
            {
                var name        = GetStr(repo, "full_name");
                var repoUrl     = GetStr(repo, "html_url");
                var description = GetStr(repo, "description");
                var stars       = repo.TryGetProperty("stargazers_count", out var s) ? s.GetInt32() : 0;
                var pushedAt    = repo.TryGetProperty("pushed_at", out var p)
                    && p.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(p.GetString(), out var dt) ? (DateTimeOffset?)dt : null;

                if (string.IsNullOrWhiteSpace(repoUrl)) continue;

                var excerpt = $"⭐ {stars:N0} stars. {description ?? "Open-source AI project"}".TrimEnd('.');
                if (excerpt.Length > 150) excerpt = excerpt[..150] + "…";

                results.Add(new SearchResult(
                    name ?? repoUrl,
                    repoUrl,
                    excerpt,
                    pushedAt,
                    Name));
            }

            logger.LogDebug("[GitHub] '{Q}' → {N} repos", query, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[GitHub] Search failed: {Q}", query);
            return [];
        }
    }

    private static string? GetStr(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
