namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// Advisory review of a finished document against axes the in-loop goal judge never checks: on-domain
/// relevance, fidelity to the originating opportunity/blueprint, and faithfulness/citation discipline.
/// Never gates generation — it surfaces drift the author (or a later job stage) can act on.
/// </summary>
public sealed record DocumentReview
{
    /// <summary>0–100: how well the document fits its domain/sub-domain and grounding source.</summary>
    public required int ReviewScore { get; init; }
    /// <summary>One-line overall judgement.</summary>
    public required string Verdict { get; init; }
    /// <summary>Specific issues found; empty when the document is on-domain and faithful.</summary>
    public required DocumentFinding[] Findings { get; init; }
    public string ModelUsed { get; init; } = string.Empty;
}

/// <summary><paramref name="Axis"/> is "relevance" | "opportunity-fidelity" | "faithfulness".
/// <paramref name="Severity"/> is "high" | "medium" | "low". <paramref name="SuggestedFix"/> is advisory prose.</summary>
public sealed record DocumentFinding(string Axis, string Severity, string Detail, string? SuggestedFix);
