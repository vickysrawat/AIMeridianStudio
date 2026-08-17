using MeridianStudio.API.Domain.Artifacts;

namespace MeridianStudio.API.Application.Contracts;

// ── Comparison ────────────────────────────────────────────────────────────────

/// <summary>POST /api/artifacts/compare — compare 2..N artifacts of the same kind.</summary>
public sealed record CompareRequest
{
    public required IReadOnlyList<string> ArtifactIds { get; init; }
}

/// <summary>One artifact column in the comparison matrix (carries provenance for interpretability — edge case #5).</summary>
public sealed record ComparisonColumn(
    string ArtifactId,
    ArtifactKind Kind,
    string? Title,
    string ModelUsed,
    int Version,
    DateTimeOffset CreatedAt);

/// <summary>One cell: the value of a dimension for a given artifact. Divergent flags a value that differs from the row's modal value.</summary>
public sealed record ComparisonCell(string ArtifactId, string? Value, bool Divergent);

public sealed record ComparisonRow(string Dimension, IReadOnlyList<ComparisonCell> Cells);

public sealed record ComparisonMatrix(
    ArtifactKind Kind,
    IReadOnlyList<ComparisonColumn> Columns,
    IReadOnlyList<ComparisonRow> Rows);

// ── Cross-run analytics ─────────────────────────────────────────────────────

public sealed record PainPointCluster(
    string Title,
    int Occurrences,
    double AvgSeverity,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> SourceArtifactIds);

public sealed record PainPointAnalytics(
    int RunsAnalyzed,
    IReadOnlyList<PainPointCluster> Clusters);

public sealed record CompetitorPattern(
    string CompetitorName,
    int Occurrences,
    IReadOnlyList<string> FeatureGaps,
    IReadOnlyList<string> SourceArtifactIds);

public sealed record CompetitorAnalytics(
    int RunsAnalyzed,
    IReadOnlyList<CompetitorPattern> Patterns);

// ── White paper (LLM-synthesized narrative from stored artifacts) ────────────

/// <summary>
/// POST /api/whitepaper — synthesize a market/competitive white paper. Driven by ONE of:
/// a Research run, a selected opportunity within it, a use-case assessment, or (legacy) arbitrary
/// artifacts. Answers: what's happening in the domain·subdomain, what other companies are working on,
/// and what we can do — with cited sources.
/// </summary>
public sealed record WhitePaperRequest
{
    /// <summary>Optional — derived from the driver (domain·subdomain / opportunity / scenario) when absent.</summary>
    public string? Title { get; init; }

    // ── Driven modes (set one) ─────────────────────────────────────────────
    /// <summary>A saved Research artifact id → whole-subdomain white paper.</summary>
    public string? ResearchArtifactId { get; init; }
    /// <summary>A PrioritizedItem.Id within the research → scenario-focused white paper. Requires ResearchArtifactId.</summary>
    public string? OpportunityId { get; init; }
    /// <summary>A use-case assessment id (cached assess-by-id) → use-case-driven white paper.</summary>
    public string? AssessmentId { get; init; }
    /// <summary>Legacy: arbitrary saved artifact ids to concatenate.</summary>
    public IReadOnlyList<string>? ArtifactIds { get; init; }

    /// <summary>Run fresh domain-aware live research (competitors/sizing/differentiation), cached. Default true.</summary>
    public bool GroundWithFreshResearch { get; init; } = true;

    /// <summary>Currently "markdown" (default). PDF/DOCX via the export endpoints.</summary>
    public string? Format { get; init; }
}

public sealed record WhitePaper
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }         // Markdown
    public required IReadOnlyList<string> SourceArtifactIds { get; init; }
    public string ModelUsed { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    /// <summary>Trust/provenance (model, providers, live sources, confidence).</summary>
    public MeridianStudio.API.Domain.Models.OutputProvenance? Provenance { get; init; }
    /// <summary>Live-source providers queried for grounding (e.g. "Tavily", "Gemini").</summary>
    public string[] SourcesQueried { get; init; } = [];
}
