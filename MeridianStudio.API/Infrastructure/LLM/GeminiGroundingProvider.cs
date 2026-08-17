using System.Net.Http.Json;
using System.Text.Json;
using MeridianStudio.API.Infrastructure.WebSearch;

namespace MeridianStudio.API.Infrastructure.LLM;

/// <summary>
/// Grounds vendor/market facts using Gemini's native Google-Search tool — it READS the real
/// pages and returns groundingMetadata (chunks → sources, supports → the actually-supported
/// statement). A SEPARATE non-JSON call (the search tool is incompatible with responseMimeType
/// JSON), so document generation stays provider-agnostic and JSON-clean. Returns the fetched
/// sources as a <see cref="LiveResearchContext"/> plus a grounded facts-brief. Throws on failure
/// so the caller can fall back to Tavily deep-fetch.
/// </summary>
public sealed class GeminiGroundingProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<GeminiGroundingProvider> logger)
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private string Model => config["LLM:Gemini:Model"] ?? "gemini-2.5-flash";

    public bool IsAvailable => !string.IsNullOrWhiteSpace(config["LLM:Gemini:ApiKey"]);

    public async Task<(LiveResearchContext Sources, string FactsBrief)> GroundAsync(
        string domain, string subDomain, IReadOnlyList<string> vendors, CancellationToken ct = default)
    {
        var apiKey = config["LLM:Gemini:ApiKey"]
            ?? throw new InvalidOperationException("LLM:Gemini:ApiKey is not set.");

        var vendorLine = vendors.Count > 0 ? string.Join(", ", vendors) : "the leading vendors in this space";
        var prompt =
            $"Report current, verifiable {domain} / {subDomain} capabilities, limitations, and pricing for: {vendorLine}. " +
            "Cite real sources. State ONLY what the sources support; do not speculate.";

        var body = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
            tools    = new[] { new { google_search = new { } } }
        };

        var url    = $"{BaseUrl}/{Model}:generateContent?key={apiKey}";
        var client = httpClientFactory.CreateClient("Gemini");

        var response = await client.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct)
            ?? throw new InvalidOperationException("Gemini grounding returned an empty body.");

        var candidate = doc.RootElement.GetProperty("candidates")[0];

        // The grounded answer text.
        var factsBrief = string.Empty;
        if (candidate.TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var parts))
        {
            factsBrief = string.Concat(parts.EnumerateArray()
                .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null)
                .Where(s => s is not null));
        }

        // groundingChunks[].web.{uri,title} → one SearchResult each (the verification handle).
        // The excerpt is the statement the source actually substantiates: groundingSupports map each
        // supported segment's text to the chunk indices backing it. Without this the sources would be
        // bare titles/URLs — the support-verifying judge would then reject every claim as cite-washed,
        // collapsing the grounded path to [REQUIRED:] placeholders. Indices reference the ORIGINAL
        // groundingChunks array, so we track original positions (non-web chunks must not shift them).
        var results = new List<SearchResult>();
        if (candidate.TryGetProperty("groundingMetadata", out var meta))
        {
            var webByIndex = new Dictionary<int, (string Uri, string Title)>();
            if (meta.TryGetProperty("groundingChunks", out var chunks)
                && chunks.ValueKind == JsonValueKind.Array)
            {
                var idx = 0;
                foreach (var chunk in chunks.EnumerateArray())
                {
                    if (chunk.TryGetProperty("web", out var web))
                    {
                        var uri   = web.TryGetProperty("uri",   out var u)  ? u.GetString()  : null;
                        var title = web.TryGetProperty("title", out var ti) ? ti.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(uri))
                            webByIndex[idx] = (uri!, string.IsNullOrWhiteSpace(title) ? uri! : title!);
                    }
                    idx++;
                }
            }

            var excerptsByIndex = new Dictionary<int, List<string>>();
            if (meta.TryGetProperty("groundingSupports", out var supports)
                && supports.ValueKind == JsonValueKind.Array)
            {
                foreach (var support in supports.EnumerateArray())
                {
                    var text = support.TryGetProperty("segment", out var seg)
                               && seg.TryGetProperty("text", out var st) ? st.GetString() : null;
                    if (string.IsNullOrWhiteSpace(text)
                        || !support.TryGetProperty("groundingChunkIndices", out var gci)
                        || gci.ValueKind != JsonValueKind.Array) continue;
                    foreach (var ix in gci.EnumerateArray())
                    {
                        if (ix.ValueKind != JsonValueKind.Number) continue;
                        var i = ix.GetInt32();
                        if (!excerptsByIndex.TryGetValue(i, out var list))
                            excerptsByIndex[i] = list = [];
                        list.Add(text!);
                    }
                }
            }

            foreach (var (i, web) in webByIndex.OrderBy(kv => kv.Key))
            {
                string? excerpt = null;
                if (excerptsByIndex.TryGetValue(i, out var spans) && spans.Count > 0)
                {
                    excerpt = string.Join(" ", spans.Distinct());
                    if (excerpt.Length > 500) excerpt = excerpt[..500].TrimEnd() + "…";
                }
                results.Add(new SearchResult(web.Title, web.Uri, excerpt, PublishedAt: null, Source: "Google Search"));
            }
        }

        logger.LogInformation("[Gemini/Ground] '{D}/{S}' → {N} grounded source(s).", domain, subDomain, results.Count);
        return (new LiveResearchContext(results, ["Google Search"], DateTimeOffset.UtcNow), factsBrief);
    }
}
