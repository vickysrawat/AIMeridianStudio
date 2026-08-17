namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// LLM-generated readiness review of a use-case brief, returned by POST /api/assessment/analyze.
/// Advisory only — it judges whether the brief is complete/specific enough to yield a strong
/// assessment and tells the user exactly what to add or sharpen. It never writes the assessment
/// and never invents facts (gaps are named; ProposedText is a template scaffold, not fabricated data).
/// </summary>
public sealed record UseCaseReadiness
{
    /// <summary>0–100: how ready the brief is to produce a high-quality assessment.</summary>
    public required int ReadinessScore { get; init; }
    /// <summary>One-line overall judgement.</summary>
    public required string Verdict { get; init; }
    /// <summary>Per-brief-field status (missing / weak / strong) with a short reason.</summary>
    public required FieldReview[] Fields { get; init; }
    /// <summary>Questions the user should answer to sharpen the brief.</summary>
    public required string[] ClarifyingQuestions { get; init; }
    /// <summary>Concrete improvements, each one-click-applicable to a specific input field.</summary>
    public required ImprovementSuggestion[] Suggestions { get; init; }
    public string ModelUsed { get; init; } = string.Empty;
}

/// <summary>Status of one brief field. <paramref name="Field"/> matches the UI input key
/// (useCaseScenario | useCase | context | problemStatement | objective | scopeOfWork | expectedOutcome).
/// <paramref name="Status"/> is "missing" | "weak" | "strong".</summary>
public sealed record FieldReview(string Field, string Status, string Comment);

/// <summary>A concrete improvement. <paramref name="Field"/> is the input key it improves;
/// <paramref name="ProposedText"/> is a paste-ready scaffold (with [e.g. …] placeholders — never
/// invented specifics) that the UI can apply into that field in one click.</summary>
public sealed record ImprovementSuggestion(string Field, string Suggestion, string? ProposedText);
