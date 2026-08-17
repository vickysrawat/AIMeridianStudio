namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// Represents a discovery initiative surfaced during AI-assisted research.
/// Urgency, Difficulty, and Value are scored 1–10.
/// </summary>
public sealed record PrioritizedItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int Urgency { get; init; }
    public required int Difficulty { get; init; }
    public required int Value { get; init; }
    public required string Rationale { get; init; }
    public required string RealLifeValue { get; init; }
    public required string IntegrationSteps { get; init; }
    /// <summary>Critical feasibility score 1–10 (0 = not yet assessed).</summary>
    public int FeasibilityScore { get; init; }
    /// <summary>2–3 sentence critical analysis of whether this solution is realistic.</summary>
    public string FeasibilityAnalysis { get; init; } = string.Empty;

    // ── 8-dimension scores (set when DimensionWeights are provided in ResearchRequest) ──
    public int? BusinessValue            { get; init; }
    public int? MarketUrgency            { get; init; }
    public int? Feasibility              { get; init; }
    public int? CompetitiveGap           { get; init; }
    public int? ImplementationDifficulty { get; init; }
    public int? RegulatoryTailwind       { get; init; }
    public int? StrategicFit             { get; init; }
    public int? AIFitness                { get; init; }

    public static PrioritizedItem Create(
        string id,
        string name,
        string description,
        int urgency,
        int difficulty,
        int value,
        string rationale,
        string realLifeValue,
        string integrationSteps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(description, nameof(description));
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale, nameof(rationale));
        ArgumentException.ThrowIfNullOrWhiteSpace(realLifeValue, nameof(realLifeValue));
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationSteps, nameof(integrationSteps));
        ArgumentOutOfRangeException.ThrowIfLessThan(urgency, 1, nameof(urgency));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(urgency, 10, nameof(urgency));
        ArgumentOutOfRangeException.ThrowIfLessThan(difficulty, 1, nameof(difficulty));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(difficulty, 10, nameof(difficulty));
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, nameof(value));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 10, nameof(value));

        return new PrioritizedItem
        {
            Id = id,
            Name = name,
            Description = description,
            Urgency = urgency,
            Difficulty = difficulty,
            Value = value,
            Rationale = rationale,
            RealLifeValue = realLifeValue,
            IntegrationSteps = integrationSteps
        };
    }
}
