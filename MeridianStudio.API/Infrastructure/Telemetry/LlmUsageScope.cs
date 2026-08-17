namespace MeridianStudio.API.Infrastructure.Telemetry;

/// <summary>Actual usage reported by a provider for the last CompleteAsync call (from the API's usage block).</summary>
public readonly record struct LlmCallUsage(
    int InputTokens,
    int OutputTokens,
    int CachedInputTokens);

/// <summary>
/// AsyncLocal side-channel so a provider can report ACTUAL token usage (including cache reads)
/// for the current call without changing <c>ILLMProvider.CompleteAsync</c>'s signature. The
/// provider sets <see cref="Current"/> after parsing its response; the telemetry decorator reads
/// it immediately after the call returns and clears it. Flows correctly because the decorator
/// awaits the inner provider in the same async context.
/// </summary>
public static class LlmUsageScope
{
    private static readonly AsyncLocal<LlmCallUsage?> _current = new();

    public static LlmCallUsage? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
