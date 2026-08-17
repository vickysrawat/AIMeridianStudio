using System.Collections.Concurrent;

namespace MeridianStudio.API.Infrastructure.Telemetry;

/// <summary>
/// In-memory <see cref="ILlmTelemetry"/>: logs every call, keeps running aggregates by
/// provider and operation, and retains a bounded ring of the most recent calls for the
/// telemetry endpoint. Cost uses an approximate per-model USD rate table (per 1M tokens)
/// that may be overridden via configuration ("Telemetry:Pricing:&lt;key&gt;:{In,Out}").
/// Resets on restart — this is a baseline-measurement aid, not a billing system.
/// </summary>
public sealed class LlmTelemetry : ILlmTelemetry
{
    private const int RecentCapacity = 200;

    // Approximate USD per 1,000,000 tokens. Keyed by a lowercase substring matched against
    // the provider/model name. First match wins; order most-specific first.
    private static readonly (string Key, double In, double Out)[] DefaultRates =
    [
        ("flash-lite", 0.10, 0.40),
        ("gemini",     0.30, 2.50),
        ("llama",      0.59, 0.79),
        ("groq",       0.59, 0.79),
        ("claude",     3.00, 15.00),
    ];

    private readonly IConfiguration _config;
    private readonly ILogger<LlmTelemetry> _logger;

    private readonly object _lock = new();
    private readonly Queue<LlmCallRecord> _recent = new(RecentCapacity);
    private readonly ConcurrentDictionary<string, Counters> _byProvider = new();
    private readonly ConcurrentDictionary<string, Counters> _byOperation = new();

    // Cache-read tokens are billed at ~10% of the normal input rate (Anthropic); a safe generic default.
    private const double CacheReadMultiplier = 0.10;

    private long _totalCalls;
    private long _totalIn;
    private long _totalOut;
    private long _totalCached;
    private long _cacheHitCalls;
    private double _totalCost;

    public LlmTelemetry(IConfiguration config, ILogger<LlmTelemetry> logger)
    {
        _config = config;
        _logger = logger;
    }

    public void Record(LlmCallRecord record)
    {
        Accumulate(_byProvider, record.Provider, record);
        Accumulate(_byOperation, record.Operation, record);

        lock (_lock)
        {
            _totalCalls++;
            _totalIn     += record.InputTokens;
            _totalOut    += record.OutputTokens;
            _totalCached += record.CachedInputTokens;
            if (record.CacheHit) _cacheHitCalls++;
            _totalCost   += record.EstimatedCostUsd;

            if (_recent.Count >= RecentCapacity) _recent.Dequeue();
            _recent.Enqueue(record);
        }

        _logger.LogInformation(
            "[LLM Cost] {Op} via {Provider} — in {In} tok (cached {Cached}), out {Out} tok, ~${Cost:F4}, {Ms} ms, {Outcome}",
            record.Operation, record.Provider, record.InputTokens, record.CachedInputTokens, record.OutputTokens,
            record.EstimatedCostUsd, record.LatencyMs, record.Success ? "ok" : "failed");
    }

    public LlmTelemetrySnapshot Snapshot()
    {
        lock (_lock)
        {
            var hitRate = _totalCalls > 0 ? Math.Round((double)_cacheHitCalls / _totalCalls, 3) : 0.0;
            return new LlmTelemetrySnapshot(
                _totalCalls, _totalIn, _totalOut, _totalCost,
                Project(_byProvider),
                Project(_byOperation),
                [.. _recent.Reverse()],
                _totalCached, hitRate);
        }
    }

    public double EstimateCostUsd(string provider, int inputTokens, int outputTokens, int cachedInputTokens = 0)
    {
        var (inRate, outRate) = RateFor(provider);
        var cached    = Math.Clamp(cachedInputTokens, 0, inputTokens);
        var fullInput = inputTokens - cached;
        return fullInput / 1_000_000.0 * inRate
             + cached    / 1_000_000.0 * inRate * CacheReadMultiplier
             + outputTokens / 1_000_000.0 * outRate;
    }

    // ── internals ──────────────────────────────────────────────────────────────

    private (double In, double Out) RateFor(string provider)
    {
        var name = provider.ToLowerInvariant();
        foreach (var (key, defIn, defOut) in DefaultRates)
        {
            if (!name.Contains(key, StringComparison.Ordinal)) continue;

            // Allow per-key override from configuration without a redeploy.
            var inRate  = _config.GetValue($"Telemetry:Pricing:{key}:In",  defIn);
            var outRate = _config.GetValue($"Telemetry:Pricing:{key}:Out", defOut);
            return (inRate, outRate);
        }
        return (0.0, 0.0); // unknown model — count tokens, cost shown as 0
    }

    private static void Accumulate(
        ConcurrentDictionary<string, Counters> map, string key, LlmCallRecord r)
    {
        var c = map.GetOrAdd(key, _ => new Counters());
        lock (c)
        {
            c.Calls++;
            c.In   += r.InputTokens;
            c.Out  += r.OutputTokens;
            c.Cost += r.EstimatedCostUsd;
        }
    }

    private static IReadOnlyList<LlmAggregate> Project(ConcurrentDictionary<string, Counters> map)
    {
        var list = new List<LlmAggregate>(map.Count);
        foreach (var kv in map)
        {
            var c = kv.Value;
            lock (c) list.Add(new LlmAggregate(kv.Key, c.Calls, c.In, c.Out, c.Cost));
        }
        return [.. list.OrderByDescending(a => a.EstimatedCostUsd)];
    }

    private sealed class Counters
    {
        public long Calls;
        public long In;
        public long Out;
        public double Cost;
    }
}
