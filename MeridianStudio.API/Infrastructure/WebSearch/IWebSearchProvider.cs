namespace MeridianStudio.API.Infrastructure.WebSearch;

/// <summary>Contract for a live web search provider used to enrich research prompts.</summary>
public interface IWebSearchProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default);
}

public sealed record SearchResult(
    string Title,
    string Url,
    string? Excerpt,           // ~150-char summary
    DateTimeOffset? PublishedAt,
    string Source              // provider display name
);

/// <summary>Aggregated live search context injected into the LLM research prompt.</summary>
public sealed record LiveResearchContext(
    IReadOnlyList<SearchResult> Results,
    string[] SourcesQueried,
    DateTimeOffset FetchedAt)
{
    public static LiveResearchContext Empty => new([], [], DateTimeOffset.UtcNow);
    public bool HasData => Results.Count > 0;
}
