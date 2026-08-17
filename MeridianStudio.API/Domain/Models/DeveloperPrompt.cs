namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// Generative developer prompt targeting a specific LLM for a component build.
/// </summary>
public sealed record DeveloperPrompt
{
    public required string Id { get; init; }
    public required string ComponentName { get; init; }
    public required string PromptText { get; init; }
    public required string TargetLLM { get; init; }
    public required string Directives { get; init; }
    public string ModelUsed { get; init; } = string.Empty;

    public static DeveloperPrompt Create(
        string id,
        string componentName,
        string promptText,
        string targetLLM,
        string directives)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName, nameof(componentName));
        ArgumentException.ThrowIfNullOrWhiteSpace(promptText, nameof(promptText));
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLLM, nameof(targetLLM));
        ArgumentException.ThrowIfNullOrWhiteSpace(directives, nameof(directives));

        return new DeveloperPrompt
        {
            Id = id,
            ComponentName = componentName,
            PromptText = promptText,
            TargetLLM = targetLLM,
            Directives = directives
        };
    }
}
