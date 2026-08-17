using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Application.Contracts;

/// <summary>POST /api/blueprint/{id}/chat — stream a section-scoped architect conversation.</summary>
public sealed record ChatMessage(string Role, string Content);

public sealed record BlueprintChatRequest
{
    /// <summary>Panel key: "arch-decisions" | "qa-scorecard" | "tech-radar" | "core-scenario" | "solution-profile" | "implementation"</summary>
    public required string SectionKey { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
}

/// <summary>
/// POST /api/research — keyword search with optional pagination.
/// Set LoadMore = true and populate ExistingItemIds to receive fresh,
/// non-duplicate PrioritizedItems beyond the initial page.
/// </summary>
/// <summary>
/// User-adjustable dimension weights for opportunity prioritization.
/// All 8 weights should sum to 100; server normalises proportionally if they don't.
/// </summary>
public sealed record DimensionWeights
{
    public int BusinessValue            { get; init; } = 20;
    public int MarketUrgency            { get; init; } = 16;
    public int Feasibility              { get; init; } = 15;
    public int CompetitiveGap           { get; init; } = 12;
    public int ImplementationDifficulty { get; init; } = 8;   // inverted in composite
    public int RegulatoryTailwind       { get; init; } = 8;
    public int StrategicFit             { get; init; } = 8;
    public int AIFitness                { get; init; } = 13;

    public int Total => BusinessValue + MarketUrgency + Feasibility + CompetitiveGap
                      + ImplementationDifficulty + RegulatoryTailwind + StrategicFit + AIFitness;

    /// <summary>Returns a normalised copy where all weights sum to 100.</summary>
    public DimensionWeights Normalised()
    {
        var total = Total;
        if (total == 100 || total == 0) return this;
        return new DimensionWeights
        {
            BusinessValue            = (int)Math.Round(BusinessValue            * 100.0 / total),
            MarketUrgency            = (int)Math.Round(MarketUrgency            * 100.0 / total),
            Feasibility              = (int)Math.Round(Feasibility              * 100.0 / total),
            CompetitiveGap           = (int)Math.Round(CompetitiveGap           * 100.0 / total),
            ImplementationDifficulty = (int)Math.Round(ImplementationDifficulty * 100.0 / total),
            RegulatoryTailwind       = (int)Math.Round(RegulatoryTailwind       * 100.0 / total),
            StrategicFit             = (int)Math.Round(StrategicFit             * 100.0 / total),
            AIFitness                = (int)Math.Round(AIFitness                * 100.0 / total),
        };
    }
}

/// <summary>Result of LLM-based domain classification + search query extraction for Use Case tab.</summary>
public sealed record UseCaseExtraction
{
    public required string Domain       { get; init; }
    public required string SubDomain    { get; init; }
    public double   Confidence          { get; init; }
    public required string CoreQuery    { get; init; }
    public string[] OptionQueries       { get; init; } = [];
    public required string ChallengeQuery  { get; init; }
    public required string CaseStudyQuery  { get; init; }
}

public sealed record ResearchRequest
{
    public required string Keywords { get; init; }
    public string? UserFeedback { get; init; }
    public bool IsRerun { get; init; }
    public bool LoadMore { get; init; }
    public int Page { get; init; } = 1;
    /// <summary>Item IDs already held by the client — used for deduplication on loadMore.</summary>
    public List<string>? ExistingItemIds { get; init; }

    // ── New structured fields for domain-based research ───────────────────────
    public string?           SubDomain { get; init; }   // e.g. "E-Discovery Optimization"
    public string?           Domain    { get; init; }   // e.g. "Law" (parent domain)
    public DimensionWeights? Weights   { get; init; }   // user-configured dimension weights
}

/// <summary>PATCH /api/blueprint/{id} — apply client overrides to a cached blueprint.</summary>
public sealed record PatchBlueprintRequest
{
    public IReadOnlyList<ArchDecision>?     ArchDecisions     { get; init; }
    public IReadOnlyList<QualityAttribute>? QualityAttributes { get; init; }
    public IReadOnlyList<TechRadarEntry>?   TechRadar         { get; init; }
    public string? CoreScenario    { get; init; }
    public string? BaseTopology    { get; init; }
    public string? DatabaseSchemes { get; init; }
    public string? EndpointManifest{ get; init; }
    public string? SolutionType           { get; init; }
    public double? SolutionTypeConfidence { get; init; }
    public string? ProjectNotes                        { get; init; }
    public IReadOnlyList<BuyVsBuildOption>? BuyVsBuild { get; init; }
    public FeasibilityAnalysis? Feasibility            { get; init; }
}

/// <summary>POST /api/generate-blueprint — compile a full SystemBlueprint for a solution.</summary>
public sealed record GenerateBlueprintRequest
{
    public required string SolutionId { get; init; }
    public required string SolutionName { get; init; }
    /// <summary>Top-level domain category, e.g. "IT Services", "Healthcare".</summary>
    public string? Domain { get; init; }
    /// <summary>Selected sub-domain, e.g. "Cloud Infrastructure", "Clinical Decision Support".</summary>
    public string? SubDomain { get; init; }
    /// <summary>
    /// Description, rationale, and real-life value from the research item.
    /// Used to specialise the blueprint to the specific opportunity within the sub-domain.
    /// </summary>
    public string? SolutionDescription { get; init; }
    /// <summary>Integration steps from the research item — the intended implementation approach.</summary>
    public string? IntegrationSteps { get; init; }
    /// <summary>Compact prioritisation signal, e.g. "Urgency 9/10 · Difficulty 7/10 · Value 10/10".</summary>
    public string? PrioritySignal { get; init; }
    /// <summary>Optional: override the auto-detected solution type (e.g. "Azure Serverless").</summary>
    public string? OverrideSolutionType { get; init; }
    /// <summary>
    /// Optional: the persisted Research artifact id. When set (with <see cref="OpportunityId"/>), the
    /// server re-fetches the full PrioritizedItem and grounds the blueprint prompt in its rich material
    /// (rationale, real-life value, feasibility, 8-dim scores, pain points, competitors) — the fidelity
    /// fix. Falls back to <see cref="SolutionDescription"/> when the research isn't persisted.
    /// </summary>
    public string? ResearchArtifactId { get; init; }
    /// <summary>Optional: the selected opportunity's id (a PrioritizedItem.Id within the research run).</summary>
    public string? OpportunityId { get; init; }
    /// <summary>
    /// Optional: user-authored pre-generation context and constraints (existing stack, compliance,
    /// team expertise, timeline…). Typically populated by acting on the readiness critic's suggestions.
    /// Woven into the blueprint/readiness prompt as an authoritative "PROJECT CONTEXT" block and persisted
    /// onto the resulting blueprint's ProjectNotes so it grounds every downstream document and chat.
    /// </summary>
    public string? ProjectNotes { get; init; }
}

/// <summary>POST /api/assessment/stream — generate a use-case Assessment from a brief.</summary>
public sealed record AssessmentRequest
{
    /// <summary>Free-form scenario (quick mode). Present when the structured brief fields are not used.</summary>
    public string? UseCaseScenario { get; init; }
    // ── Structured brief ──────────────────────────────────────────────────────
    public string? UseCase { get; init; }
    public string? Context { get; init; }
    public string? ProblemStatement { get; init; }
    public string? Objective { get; init; }
    public string? ScopeOfWork { get; init; }
    public string? ExpectedOutcome { get; init; }
    public string? Domain { get; init; }
    /// <summary>
    /// When true (default), the assessment runs live web search FIRST and grounds the LLM in the
    /// fetched sources. Set false to skip search and generate from the brief alone. Honoured only
    /// when a web-search provider is configured.
    /// </summary>
    public bool GroundInLiveResearch { get; init; } = true;
}

/// <summary>POST /api/documents/freshness — is a document still current with its grounding blueprint?</summary>
public sealed record DocumentFreshnessRequest
{
    public string? BlueprintId { get; init; }
    /// <summary>The document's <c>groundedBlueprintFingerprint</c> captured at generation.</summary>
    public string? GroundedFingerprint { get; init; }
}

/// <summary>POST /api/documents/review — advisory domain/opportunity review of a finished document.</summary>
public sealed record DocumentReviewRequest
{
    public required string Content { get; init; }
    public string? Title { get; init; }
    public string? Domain { get; init; }
    public string? SubDomain { get; init; }
    public string? TemplateType { get; init; }
    /// <summary>Optional grounding source — the review anchors on the blueprint (or assessment) if resolvable.</summary>
    public string? BlueprintId { get; init; }
    public string? AssessmentId { get; init; }
}

/// <summary>PATCH /api/assessment/{id} — apply client/chat overrides to a cached assessment.</summary>
public sealed record PatchAssessmentRequest
{
    public string? ExecutiveSummary { get; init; }
    public IReadOnlyList<AssessmentSection>?     Sections             { get; init; }
    public string[]? Recommendations { get; init; }
    public string[]? Risks { get; init; }
    public string[]? NextSteps { get; init; }
    public FeasibilityAnalysis? Feasibility { get; init; }
    public IReadOnlyList<RecommendedDocument>?   RecommendedDocuments { get; init; }
}

/// <summary>POST /api/execute-task — synthesise step-by-step execution logs and a code template.</summary>
public sealed record ExecuteTaskRequest
{
    public required string TaskName { get; init; }
    public string? SystemicValue { get; init; }
    public string? EstimatedEffort { get; init; }
    public string? Context { get; init; }
    /// <summary>Target language for the generated code scaffold. Supported: csharp (default), typescript, python, java, go.</summary>
    public string? Language { get; init; } = "csharp";
    /// <summary>
    /// Optional: ground the generated code in a blueprint's design (tech stack, endpoints, schema, resilience)
    /// so execution consumes the actual design instead of a generic scaffold. Lineage-tags the TaskSpec.
    /// </summary>
    public string? BlueprintId { get; init; }
    /// <summary>Optional: the use-case Assessment id (reserved for assessment-grounded execution).</summary>
    public string? AssessmentId { get; init; }
}

/// <summary>
/// Competitor insight from the research phase, forwarded to market-analysis document generation
/// so the LLM uses real researched competitors instead of inventing them.
/// </summary>
public sealed record CompetitorInsightDto(
    string CompetitorName,
    string FeatureGap,
    string ImpactScore,
    string StrategicPlaybook);

/// <summary>A real research source used to ground document generation (Phase 4).</summary>
public sealed record ResearchSourceDto(
    string Title,
    string? Url = null,
    string? Source = null,
    string? Excerpt = null);

/// <summary>POST /api/documents/fix — repair one section of a structured document by criterion id.</summary>
public sealed record FixSectionRequest
{
    public required MeridianStudio.API.Domain.Models.StructuredDocument Document { get; init; }
    public required string CriterionId { get; init; }
}

/// <summary>POST /api/generate-document — compile a corporate Markdown document.</summary>
public sealed record GenerateDocumentRequest
{
    /// <summary>Source blueprint id. Either BlueprintId or AssessmentId must be set.</summary>
    public string? BlueprintId { get; init; }
    /// <summary>Source assessment id (use-case workflow). Either BlueprintId or AssessmentId must be set.</summary>
    public string? AssessmentId { get; init; }
    /// <summary>Non-null grounding id — the blueprint id, else the assessment id, else "".</summary>
    public string SourceId => BlueprintId ?? AssessmentId ?? string.Empty;
    public required string Title { get; init; }
    /// <summary>executive-summary | market-analysis | technical-specification | proposal | governance-adr | developer-handbook | detailed-design</summary>
    public required string TemplateType { get; init; }
    public string? Domain { get; init; }
    /// <summary>Specific sub-domain within the domain (e.g. "Clinical Decision Support" within "Healthcare AI"). Drives sub-domain-specific few-shot example retrieval.</summary>
    public string? SubDomain { get; init; }
    public string? SolutionType { get; init; }
    /// <summary>First ~1 500 chars of the compiled blueprint's coreScenario — grounds the LLM in the correct tech stack and architecture.</summary>
    public string? BlueprintContext { get; init; }
    /// <summary>When true, evicts the cached document and forces a fresh LLM call.</summary>
    public bool IsRerun { get; init; }
    // ── Mission fields (from user selection, possibly refined) ────────────────
    public string? SelectedTone { get; init; }
    public string? SelectedGoal { get; init; }
    public string[]? SelectedCriteria { get; init; }
    public bool WasRefined { get; init; }
    // ── Market-analysis grounding ─────────────────────────────────────────────
    /// <summary>
    /// Real competitor insights from the Research phase.
    /// Provided only for market-analysis documents.
    /// The LLM is instructed to use ONLY these competitors and must not invent others.
    /// </summary>
    public CompetitorInsightDto[]? CompetitorInsights { get; init; }

    /// <summary>
    /// Real research sources (from the Research phase) to ground the document in. When supplied
    /// they are injected as a budgeted, SOURCE-tagged tier and the model is told to cite them.
    /// Optional — when absent the document is grounded only in the blueprint contract.
    /// </summary>
    public ResearchSourceDto[]? ResearchSources { get; init; }

    /// <summary>
    /// Synthesised, cross-vendor facts-brief from live Gemini Google-Search grounding — the actual
    /// grounded statements, cited to the same [S#] sources in <see cref="ResearchSources"/>. Injected
    /// as a grounded-context block so the model cites real facts, not just source titles. Server-set.
    /// </summary>
    public string? GroundedFacts { get; init; }

    /// <summary>
    /// When true (default), fact-heavy templates (market-analysis, executive-summary, proposal)
    /// run live web grounding before generation so vendor/market claims are cited [S#]. The
    /// per-operation cost control — off skips the grounding fee; honesty rules still force [REQUIRED:].
    /// </summary>
    public bool GroundInLiveResearch { get; init; } = true;

  /// <summary>
  /// When provided, the goal-directed loop skips the full first-pass generation
  /// and immediately patches this content against the failed criteria.
  /// </summary>
  public string? ExistingContent { get; init; }

  /// <summary>
  /// Pre-populated failure reasons from a prior generation run.
  /// Fed directly into BuildDocumentPatch so the patch prompt has specific
  /// actionable context without needing to re-evaluate from scratch.
  /// </summary>
  public Dictionary<string, string>? KnownFailureReasons { get; init; }
}

/// <summary>POST /api/mission-suggestions — generate contextual tone, goal, and criteria options.</summary>
public sealed record MissionSuggestionsRequest
{
    public required string TemplateType { get; init; }
    public string? Domain { get; init; }
    public string? SolutionType { get; init; }
    /// <summary>First ~800 chars of coreScenario to ground suggestions in the actual solution.</summary>
    public string? BlueprintContext { get; init; }
    /// <summary>Populated server-side from SelectionBankService before calling LLM.</summary>
    public string? PastSelectionsContext { get; init; }
}

/// <summary>POST /api/mission-suggestions/record — record a user's mission selection as a training signal.</summary>
public sealed record RecordSelectionRequest
{
    public required string TemplateType { get; init; }
    public string? Domain { get; init; }
    public string? SolutionType { get; init; }
    public required string SelectedTone { get; init; }
    public required string SelectedGoal { get; init; }
    public required string[] SelectedCriteria { get; init; }
    public bool WasRefined { get; init; }
}

/// <summary>POST /api/generate-component-prompt — compile a developer handoff prompt for an LLM.</summary>
public sealed record GenerateComponentPromptRequest
{
    public required string ComponentName { get; init; }
    /// <summary>Claude Sonnet | GPT-4o | Gemini 1.5 Pro — defaults to "Claude Sonnet" if omitted.</summary>
    public string? TargetLLM { get; init; }
    public string? Context { get; init; }
}

/// <summary>
/// POST /api/generate-project — package a complete, runnable application as a zip archive.
/// StepCodes are the LLM/heuristic-generated code bodies (one per integration step).
/// The endpoint wraps them in a full project scaffold and returns application/zip.
/// </summary>
public sealed record GenerateProjectRequest
{
    public required string SolutionName     { get; init; }
    public string?         Description      { get; init; }
    /// <summary>Integration step titles (from the PrioritizedItem).</summary>
    public string[]        IntegrationSteps { get; init; } = [];
    /// <summary>Generated code per step — aligned by index with IntegrationSteps.</summary>
    public string[]        StepCodes        { get; init; } = [];
    /// <summary>csharp (default) | typescript | python | java | go</summary>
    public string?         Language         { get; init; } = "csharp";
    public string?         Domain           { get; init; }
    public string?         RealLifeValue    { get; init; }
}
