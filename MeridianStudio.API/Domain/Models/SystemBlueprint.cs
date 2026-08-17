namespace MeridianStudio.API.Domain.Models;

public sealed record ArchDecision(
    string   Decision,
    string   ChosenApproach,
    string?  Rationale                  = null,
    string[] AlternativesConsidered     = default!,
    string[] Risks                      = default!
);

public sealed record QualityAttribute(
    string  Attribute,
    string? Target      = null,
    string? Measurement = null
);

public sealed record TechRadarEntry(
    string   Layer,
    string[] Technologies = default!
);

public sealed record BuyVsBuildOption(
    string  Component,
    string  BuyOption,
    string  BuyRationale,
    string  BuildApproach,
    string  BuildRationale,
    string  Recommendation,      // "Buy" | "Build" | "Hybrid"
    string? RecommendationReason = null
);

/// <summary>One candidate option in a use-case feasibility comparison (e.g. "Replicate to AWS").</summary>
public sealed record FeasibilityOption(
    string   Name,
    string   Verdict,              // "Feasible" | "Feasible with effort" | "Partial" | "Not recommended"
    int      Score             = 5,// 1–10
    string?  EffortEstimate    = null,
    string[] Challenges        = default!,
    string[] Roadblocks        = default!,
    string?  Recommendation    = null   // one concise sentence
);

/// <summary>
/// Feasibility / decision analysis for a free-form use-case scenario. Populated only
/// when the blueprint was generated from a UseCaseScenario; compares options side by side
/// and answers the user's core concern directly.
/// </summary>
public sealed record FeasibilityAnalysis(
    string?  UseCase               = null,  // the original scenario, echoed back
    string?  Summary               = null,  // overall narrative verdict
    string?  PrimaryConcernVerdict = null,  // direct answer to the core worry
    IReadOnlyList<FeasibilityOption>? Options = null
);

/// <summary>
/// Compiled technical design document for a proposed AI solution.
/// </summary>
public sealed record SystemBlueprint
{
    public required string Id { get; init; }
    public required string SolutionId { get; init; }
    public required string SolutionName { get; init; }
    public required string Domain { get; init; }
    public required string CoreScenario { get; init; }
    public required string BaseTopology { get; init; }
    public required string DatabaseSchemes { get; init; }
    public required string EndpointManifest { get; init; }
    public required string ResilienceStrategies { get; init; }
    public string ModelUsed { get; init; } = string.Empty;

    // ── Sub-domain context ───────────────────────────────────────────────────
    /// <summary>Selected sub-domain, e.g. "Cloud Infrastructure".</summary>
    public string SubDomain { get; init; } = string.Empty;
    /// <summary>Rich description from the research item — drives opportunity specialisation.</summary>
    public string SolutionDescription { get; init; } = string.Empty;

    // ── Solution type classification ──────────────────────────────────────────
    /// <summary>Detected solution type (e.g. "REST API", "Azure Serverless"). Empty until classified.</summary>
    public string SolutionType { get; init; } = string.Empty;
    /// <summary>Classifier confidence 0.0–1.0.</summary>
    public double SolutionTypeConfidence { get; init; }

    // ── WHY layer ────────────────────────────────────────────────────────────
    public IReadOnlyList<ArchDecision>     ArchDecisions     { get; init; } = [];
    public IReadOnlyList<QualityAttribute> QualityAttributes { get; init; } = [];
    public IReadOnlyList<TechRadarEntry>   TechRadar         { get; init; } = [];

    // ── Buy vs Build ──────────────────────────────────────────────────────────
    public IReadOnlyList<BuyVsBuildOption> BuyVsBuild { get; init; } = [];

    // ── Feasibility analysis (use-case-driven blueprints only) ─────────────────
    /// <summary>Side-by-side feasibility comparison. Null for research-driven blueprints.</summary>
    public FeasibilityAnalysis? Feasibility { get; init; }

    // ── Project-specific context (user-authored) ──────────────────────────────
    /// <summary>
    /// Free-form notes added by the user: client constraints, existing infrastructure,
    /// team expertise, compliance obligations, budget, etc. Included verbatim in
    /// document generation and chat prompts so AI output reflects the actual project.
    /// </summary>
    public string ProjectNotes { get; init; } = string.Empty;

    public static SystemBlueprint Create(
        string id,
        string solutionId,
        string solutionName,
        string domain,
        string coreScenario,
        string baseTopology,
        string databaseSchemes,
        string endpointManifest,
        string resilienceStrategies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionId, nameof(solutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionName, nameof(solutionName));
        ArgumentException.ThrowIfNullOrWhiteSpace(domain, nameof(domain));
        ArgumentException.ThrowIfNullOrWhiteSpace(coreScenario, nameof(coreScenario));
        ArgumentException.ThrowIfNullOrWhiteSpace(baseTopology, nameof(baseTopology));
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseSchemes, nameof(databaseSchemes));
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointManifest, nameof(endpointManifest));
        ArgumentException.ThrowIfNullOrWhiteSpace(resilienceStrategies, nameof(resilienceStrategies));

        return new SystemBlueprint
        {
            Id = id,
            SolutionId = solutionId,
            SolutionName = solutionName,
            Domain = domain,
            CoreScenario = coreScenario,
            BaseTopology = baseTopology,
            DatabaseSchemes = databaseSchemes,
            EndpointManifest = endpointManifest,
            ResilienceStrategies = resilienceStrategies
        };
    }
}
