namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// A documented problem that companies in a domain are actively experiencing.
/// Pain points are the source of opportunities — each links to the items that address it.
/// </summary>
public sealed record PainPoint
{
    public required string Id               { get; init; }  // unique 8-char id
    public required string Title            { get; init; }  // e.g. "Legacy ITSM can't handle AI alert volumes"
    public required string Description      { get; init; }  // 2-sentence explanation
    public required string AffectedSegment  { get; init; }  // e.g. "Enterprise IT Operations teams"
    public int    Severity  { get; init; }                  // 1–10
    public string Frequency { get; init; } = "Common";      // "Widespread" | "Common" | "Occasional"
    public string[] RelatedOpportunityIds { get; init; } = [];
    public string? LiveSource { get; init; }                // article title or URL that evidenced this pain
}
