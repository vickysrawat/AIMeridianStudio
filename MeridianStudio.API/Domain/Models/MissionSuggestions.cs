namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// LLM-generated mission suggestions returned by POST /api/mission-suggestions.
/// All three option lists are contextual — grounded in domain + solutionType + documentType.
/// </summary>
public sealed record MissionSuggestions
{
    public required string Persona { get; init; }
    public required string SecondaryAudience { get; init; }
    public required ToneOption[] ToneOptions { get; init; }
    public required GoalOption[] GoalOptions { get; init; }
    public required CriteriaOption[] CriteriaOptions { get; init; }
    public string ModelUsed { get; init; } = string.Empty;
}

/// <summary>A selectable tone with a short UI label and the full phrase sent to the LLM.</summary>
public sealed record ToneOption
{
    public required string Label { get; init; }
    public required string FullPhrase { get; init; }
}

/// <summary>A selectable goal with a short label and the full goal text.</summary>
public sealed record GoalOption
{
    public required string Label { get; init; }
    public required string Text { get; init; }
}

/// <summary>A selectable set of pass/fail evaluation criteria.</summary>
public sealed record CriteriaOption
{
    public required string Label { get; init; }
    public required string[] Criteria { get; init; }
}
