namespace MeridianStudio.API.Infrastructure.Telemetry;

/// <summary>
/// One measured LLM provider call: token counts, estimated cost, latency, outcome.
/// Token counts are proxy estimates (see <see cref="Tokenization.ITokenCounter"/>);
/// cost is derived from an approximate per-model rate table.
/// </summary>
public sealed record LlmCallRecord(
    string Provider,
    string Operation,
    int InputTokens,
    int OutputTokens,
    double EstimatedCostUsd,
    long LatencyMs,
    bool Success,
    DateTimeOffset TimestampUtc,
    int CachedInputTokens = 0)
{
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>True when this call served part of its input from a prompt cache (B1 savings evidence).</summary>
    public bool CacheHit => CachedInputTokens > 0;
}
