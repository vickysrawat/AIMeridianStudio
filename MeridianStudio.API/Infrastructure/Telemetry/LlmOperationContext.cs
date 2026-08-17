namespace MeridianStudio.API.Infrastructure.Telemetry;

/// <summary>
/// Ambient operation name for the LLM call currently in flight. The orchestrator sets
/// this around each provider attempt so the telemetry decorator can attribute a call to
/// its operation ("generate-document", "judge-document", …) without the providers needing
/// to know about operations. Flows across awaits via <see cref="AsyncLocal{T}"/>.
/// </summary>
public static class LlmOperationContext
{
    private static readonly AsyncLocal<string?> _current = new();

    public static string Current
    {
        get => _current.Value ?? "unknown";
        set => _current.Value = value;
    }
}
