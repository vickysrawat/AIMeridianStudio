using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MeridianStudio.API.Infrastructure.WebSearch;

/// <summary>
/// Serper — Google Search API. Used for the competitive landscape group only
/// because Google's index has superior coverage of vendors, startups, and funding rounds.
/// API key: config["WebSearch:Serper:ApiKey"]
/// </summary>
public sealed class SerperSearchProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<SerperSearchProvider> logger) : IWebSearchProvider
{
    private const string Endpoint = "https://google.serper.dev/news";

    public string Name => "Serper";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["WebSearch:Serper:ApiKey"]);

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, CancellationToken ct = default)
    {
        var apiKey = config["WebSearch:Serper:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return [];

        var maxResults = config.GetValue<int>("WebSearch:MaxResultsPerQuery", 5);

        var body = new { q = query, num = maxResults, hl = "en", gl = "us" };

        try
        {
            var client = httpClientFactory.CreateClient("Serper");
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = JsonContent.Create(body)
            };
            req.Headers.Add("X-API-KEY", apiKey);

            var response = await client.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var results = new List<SearchResult>();

            // Serper news response has a "news" array
            if (!doc.RootElement.TryGetProperty("news", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in arr.EnumerateArray())
            {
                var title   = GetStr(item, "title");
                var url     = GetStr(item, "link");
                var snippet = GetStr(item, "snippet");
                var dateStr = GetStr(item, "date");
                DateTimeOffset? published = dateStr is not null
                    && DateTimeOffset.TryParse(dateStr, out var dt) ? dt : null;

                if (string.IsNullOrWhiteSpace(url)) continue;

                var excerpt = string.IsNullOrWhiteSpace(snippet) ? null
                    : snippet.Length > 150 ? snippet[..150].TrimEnd() + "…"
                    : snippet;

                results.Add(new SearchResult(title ?? url, url, excerpt, published, Name));
            }

            logger.LogDebug("[Serper] '{Q}' → {N} results", query, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Serper] Search failed: {Q}", query);
            return [];
        }
    }

    private static string? GetStr(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
