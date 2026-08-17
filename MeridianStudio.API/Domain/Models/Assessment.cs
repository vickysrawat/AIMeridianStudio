namespace MeridianStudio.API.Domain.Models;

/// <summary>One concise, adaptive section of an assessment (Markdown body).</summary>
public sealed record AssessmentSection(string Title, string Body);

/// <summary>
/// A deep deliverable the assessment recommends generating on demand as a Document.
/// TemplateType must be one of the existing document template types.
/// </summary>
public sealed record RecommendedDocument(
    string ExpectedOutcome,
    string Title,
    string TemplateType,   // executive-summary | market-analysis | technical-specification | proposal | governance-adr | developer-handbook | detailed-design
    string Rationale);

/// <summary>
/// A standalone, use-case-shaped assessment produced by the Use Case workflow.
/// Unlike <see cref="SystemBlueprint"/> it carries no application-development skeleton —
/// its <see cref="Sections"/> adapt to the brief's Expected Outcome. The heavy per-outcome
/// deliverables are generated separately as Documents (see <see cref="RecommendedDocuments"/>).
/// </summary>
public sealed record Assessment
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Domain { get; init; } = string.Empty;

    // ── Echoed brief ──────────────────────────────────────────────────────────
    public string UseCase { get; init; } = string.Empty;
    public string Context { get; init; } = string.Empty;
    public string ProblemStatement { get; init; } = string.Empty;
    public string Objective { get; init; } = string.Empty;
    public string ScopeOfWork { get; init; } = string.Empty;
    public string ExpectedOutcome { get; init; } = string.Empty;

    // ── Outcome (concise) ─────────────────────────────────────────────────────
    /// <summary>Answers the Objective and states whether the Expected Outcome is achievable.</summary>
    public required string ExecutiveSummary { get; init; }
    /// <summary>Concise adaptive sections (strategy + roadmap outline) shaped by the Expected Outcome.</summary>
    public IReadOnlyList<AssessmentSection> Sections { get; init; } = [];
    public string[] Recommendations { get; init; } = [];
    public string[] Risks { get; init; } = [];
    public string[] NextSteps { get; init; } = [];

    /// <summary>Side-by-side options comparison when the assessment weighs alternatives.</summary>
    public FeasibilityAnalysis? Feasibility { get; init; }

    /// <summary>Deep deliverables to generate on demand as Documents, one per Expected Outcome.</summary>
    public IReadOnlyList<RecommendedDocument> RecommendedDocuments { get; init; } = [];

    public string ModelUsed { get; init; } = string.Empty;
}
