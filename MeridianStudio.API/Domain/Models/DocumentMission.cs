namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// The resolved mission for a document generation request.
/// Persona and SecondaryAudience come from PersonaRegistry (static).
/// SelectedTone, SelectedGoal, and SelectedCriteria come from the user's
/// selection of LLM-generated suggestions (possibly refined).
/// </summary>
public sealed record DocumentMission
{
    public required string TemplateType { get; init; }
    /// <summary>Who is writing this document (the LLM thinks AS this person).</summary>
    public required string Persona { get; init; }
    /// <summary>Secondary audience the document must also serve.</summary>
    public required string SecondaryAudience { get; init; }
    /// <summary>Full tone phrase injected into the system prompt.</summary>
    public required string SelectedTone { get; init; }
    /// <summary>The specific outcome this document must achieve.</summary>
    public required string SelectedGoal { get; init; }
    /// <summary>Pass/fail criteria the judge evaluates against.</summary>
    public required string[] SelectedCriteria { get; init; }
    /// <summary>True when the user edited any suggestion before generating.</summary>
    public required bool WasRefined { get; init; }
}
