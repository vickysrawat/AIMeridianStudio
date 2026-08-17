namespace MeridianStudio.API.Infrastructure.Telemetry;

/// <summary>
/// Sink for per-call LLM cost/token telemetry. Records each measured provider call and
/// exposes a rolling snapshot for the telemetry endpoint. Register as a singleton.
/// </summary>
public interface ILlmTelemetry
{
    /// <summary>Records one measured call and updates running aggregates.</summary>
    void Record(LlmCallRecord record);

    /// <summary>Returns the current aggregate snapshot plus the most recent calls.</summary>
    LlmTelemetrySnapshot Snapshot();

    /// <summary>
    /// Estimated USD cost for a call. <paramref name="cachedInputTokens"/> (a subset of
    /// <paramref name="inputTokens"/>) is billed at a discounted cache-read rate (B1/B4).
    /// </summary>
    double EstimateCostUsd(string provider, int inputTokens, int outputTokens, int cachedInputTokens = 0);
}

/// <summary>Aggregate view over all recorded calls this session.</summary>
public sealed record LlmTelemetrySnapshot(
    long TotalCalls,
    long TotalInputTokens,
    long TotalOutputTokens,
    double TotalEstimatedCostUsd,
    IReadOnlyList<LlmAggregate> ByProvider,
    IReadOnlyList<LlmAggregate> ByOperation,
    IReadOnlyList<LlmCallRecord> RecentCalls,
    long TotalCachedInputTokens = 0,
    double CacheHitRate = 0.0);

/// <summary>Roll-up of calls grouped by a key (provider or operation).</summary>
public sealed record LlmAggregate(
    string Key,
    long Calls,
    long InputTokens,
    long OutputTokens,
    double EstimatedCostUsd);
