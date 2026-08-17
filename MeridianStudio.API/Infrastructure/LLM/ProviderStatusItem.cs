namespace MeridianStudio.API.Infrastructure.LLM;

/// <summary>
/// Snapshot of a single LLM provider's configuration and runtime state,
/// returned by GET /api/providers/status.
/// </summary>
/// <param name="Name">Display name, e.g. "Gemini (gemini-2.5-flash)".</param>
/// <param name="Priority">Position in the cascade chain (1 = highest priority).</param>
/// <param name="Configured">True when an API key is present in configuration.</param>
/// <param name="Status">
/// Runtime state:
///   "active"         — last call succeeded;
///   "idle"           — configured but no calls made yet this session;
///   "failed"         — last call returned an unexpected error;
///   "quota"          — last call was rate-limited (HTTP 429);
///   "unavailable"    — last call returned HTTP 503 (provider down or wrong model name);
///   "not-configured" — no API key set;
///   "fallback"       — this is the offline heuristic engine and it was invoked.
/// </param>
/// <param name="Reason">Human-readable explanation shown in the UI.</param>
public sealed record ProviderStatusItem(
    string Name,
    int    Priority,
    bool   Configured,
    string Status,
    string Reason);
