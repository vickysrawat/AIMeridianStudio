using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MeridianStudio.API.Infrastructure.WebSearch;

/// <summary>
/// Tavily AI-optimised search provider — purpose-built for LLM augmentation.
/// Returns clean, pre-processed summaries suitable for direct prompt injection.
/// API key: config["WebSearch:Tavily:ApiKey"]
/// </summary>
public sealed class TavilySearchProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<TavilySearchProvider> logger) : IWebSearchProvider
{
    private const string Endpoint = "https://api.tavily.com/search";

    public string Name => "Tavily";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["WebSearch:Tavily:ApiKey"]);

    /// <param name="query">Search query string.</param>
    /// <param name="daysBack">Restrict results to this many days. Defaults to config value.</param>
    /// <param name="topic">"news" (default) or "general" — use "general" for capability/pricing facts.</param>
    /// <param name="deep">When true, document-grounding mode: pulls raw content + larger excerpts
    /// (~600 chars) so citations can be substantiated rather than judged against a 150-char blurb.</param>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, CancellationToken ct = default, int? daysBack = null,
        string topic = "news", bool deep = false)
    {
        var apiKey = config["WebSearch:Tavily:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return [];

        var days       = daysBack ?? config.GetValue<int>("WebSearch:DaysBack", 90);
        var maxResults = config.GetValue<int>("WebSearch:MaxResultsPerQuery", 5);
        var excerptCap = deep ? 600 : 150;

        var body = new
        {
            api_key             = apiKey,
            query               = query,
            search_depth        = "advanced",
            topic               = topic,
            days                = days,
            max_results         = maxResults,
            include_answer      = false,
            include_raw_content = deep
        };

        try
        {
            var client   = httpClientFactory.CreateClient("Tavily");
            var response = await client.PostAsJsonAsync(Endpoint, body, ct);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var results = new List<SearchResult>();
            if (!doc.RootElement.TryGetProperty("results", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in arr.EnumerateArray())
            {
                var title     = GetStr(item, "title");
                var url       = GetStr(item, "url");
                var content   = GetStr(item, "content");
                var published = ParseDate(item, "published_date");

                if (string.IsNullOrWhiteSpace(url)) continue;

                var excerpt = string.IsNullOrWhiteSpace(content) ? null
                    : content.Length > excerptCap ? content[..excerptCap].TrimEnd() + "…"
                    : content;

                results.Add(new SearchResult(title ?? url, url, excerpt, published, Name));
            }

            logger.LogDebug("[Tavily] '{Q}' ({D}d) → {N} results", query, days, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Tavily] Search failed: {Q}", query);
            return [];
        }
    }

    // IWebSearchProvider default implementation
    Task<IReadOnlyList<SearchResult>> IWebSearchProvider.SearchAsync(
        string query, CancellationToken ct)
        => SearchAsync(query, ct);

    private static string? GetStr(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static DateTimeOffset? ParseDate(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String) return null;
        return DateTimeOffset.TryParse(v.GetString(), out var dt) ? dt : null;
    }
}
