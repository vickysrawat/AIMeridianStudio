using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Configuration;

namespace MeridianStudio.API.Infrastructure.WebSearch;

/// <summary>
/// PubMed free search API — no API key required.
/// Used for Healthcare and Pharmaceutical domains to surface clinical AI research
/// evidence for Feasibility and AI Fitness scoring.
/// Rate limit: 3 req/sec unauthenticated.
/// </summary>
public sealed class PubMedSearchProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<PubMedSearchProvider> logger) : IWebSearchProvider
{
    private const string SearchBase   = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esearch.fcgi";
    private const string SummaryBase  = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esummary.fcgi";

    public string Name => "PubMed";
    public bool IsConfigured => true;  // free, always available

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, CancellationToken ct = default)
    {
        var maxResults = config.GetValue<int>("WebSearch:MaxResultsPerQuery", 5);

        try
        {
            var client = httpClientFactory.CreateClient();

            // Step 1: search for IDs
            var searchUrl = $"{SearchBase}?db=pubmed&term={HttpUtility.UrlEncode(query + " artificial intelligence")}" +
                            $"&datetype=pdat&reldate=730&retmax={maxResults}&retmode=json";

            var searchResp = await client.GetAsync(searchUrl, ct);
            searchResp.EnsureSuccessStatusCode();

            using var searchDoc = await JsonDocument.ParseAsync(
                await searchResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var ids = searchDoc.RootElement
                .GetProperty("esearchresult")
                .GetProperty("idlist")
                .EnumerateArray()
                .Select(e => e.GetString())
                .Where(id => id is not null)
                .ToArray();

            if (ids.Length == 0) return [];

            // Step 2: fetch summaries for the IDs
            var idsParam   = string.Join(",", ids);
            var summaryUrl = $"{SummaryBase}?db=pubmed&id={idsParam}&retmode=json";

            var summaryResp = await client.GetAsync(summaryUrl, ct);
            summaryResp.EnsureSuccessStatusCode();

            using var summaryDoc = await JsonDocument.ParseAsync(
                await summaryResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var results = new List<SearchResult>();
            var uids    = summaryDoc.RootElement.GetProperty("result").GetProperty("uids");

            foreach (var uid in uids.EnumerateArray())
            {
                var id   = uid.GetString();
                if (id is null) continue;

                var article = summaryDoc.RootElement.GetProperty("result").GetProperty(id);
                var title   = article.TryGetProperty("title",     out var t) ? t.GetString() : null;
                var source  = article.TryGetProperty("source",    out var s) ? s.GetString() : "PubMed";
                var dateStr = article.TryGetProperty("pubdate",   out var d) ? d.GetString() : null;
                DateTimeOffset? published = dateStr is not null
                    && DateTimeOffset.TryParse(dateStr, out var dt) ? dt : null;

                var url = $"https://pubmed.ncbi.nlm.nih.gov/{id}/";
                results.Add(new SearchResult(
                    title ?? $"PubMed article {id}",
                    url,
                    $"[{source}] Clinical AI research paper — see PubMed for abstract.",
                    published,
                    Name));
            }

            logger.LogDebug("[PubMed] '{Q}' → {N} results", query, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PubMed] Search failed: {Q}", query);
            return [];
        }
    }
}
