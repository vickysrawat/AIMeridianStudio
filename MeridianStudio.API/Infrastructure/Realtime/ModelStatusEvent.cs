namespace MeridianStudio.API.Infrastructure.Realtime;

/// <summary>
/// A single model-routing event broadcast to all connected SSE clients.
/// </summary>
/// <param name="Type">
///   "connected"   — initial handshake on SSE subscribe<br/>
///   "attempting"  — orchestrator is trying a provider<br/>
///   "succeeded"   — provider returned a valid result<br/>
///   "failed"      — provider failed; rotating to next<br/>
///   "fallback"    — all providers exhausted; using local heuristic engine
/// </param>
/// <param name="Provider">Human-readable provider/model name.</param>
/// <param name="Operation">API operation name, e.g. "research" or "generate-blueprint".</param>
/// <param name="Timestamp">UTC timestamp of the event.</param>
/// <param name="Detail">Optional human-readable reason (e.g. why a provider failed / why the engine fell back).</param>
public sealed record ModelStatusEvent(
    string Type,
    string Provider,
    string Operation,
    DateTimeOffset Timestamp,
    string? Detail = null);
