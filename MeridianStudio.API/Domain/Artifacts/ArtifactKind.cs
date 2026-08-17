namespace MeridianStudio.API.Domain.Artifacts;

/// <summary>
/// The generated artifact types the API produces and can persist.
/// Serialised as camelCase strings (matches the global JSON enum converter).
/// </summary>
public enum ArtifactKind
{
    Research,
    Blueprint,
    TaskSpec,
    Document,
    DeveloperPrompt,
    Assessment
}
