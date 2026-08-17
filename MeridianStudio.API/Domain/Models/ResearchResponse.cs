namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// Main payload returned after an AI-assisted domain discovery session.
/// </summary>
public sealed record ResearchResponse
{
    public required string Domain { get; init; }
    public required List<string> DomainsList { get; init; }
    public required List<CompetitorInsight> CompetitorInsights { get; init; }
    public required List<PrioritizedItem> Items { get; init; }
    public string ModelUsed { get; init; } = string.Empty;

    // ── Live enrichment metadata ──────────────────────────────────────────────
    public IReadOnlyList<PainPoint> PainPoints       { get; init; } = [];
    public string[] LiveSourcesQueried               { get; init; } = [];

    /// <summary>Trust/provenance metadata (model, providers tried, sources, confidence). Null on legacy paths.</summary>
    public OutputProvenance? Provenance              { get; init; }

    public static ResearchResponse Create(
        string domain,
        List<string>? domainsList = null,
        List<CompetitorInsight>? competitorInsights = null,
        List<PrioritizedItem>? items = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain, nameof(domain));

        return new ResearchResponse
        {
            Domain = domain,
            DomainsList = domainsList ?? [],
            CompetitorInsights = competitorInsights ?? [],
            Items = items ?? []
        };
    }
}
