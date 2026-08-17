using System.Text.Json;

namespace MeridianStudio.API.Infrastructure.Telemetry;

/// <summary>
/// <see cref="ILlmTelemetry"/> decorator that persists each call to an append-only JSONL file and
/// replays it on startup so cost/token aggregates survive restarts (B4). Registered only when
/// <c>Telemetry:Persist</c> is true. Delegates all aggregation to the inner in-memory telemetry.
/// </summary>
public sealed class PersistentLlmTelemetry : ILlmTelemetry
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    private readonly ILlmTelemetry _inner;
    private readonly ILogger<PersistentLlmTelemetry> _logger;
    private readonly string _path;
    private readonly object _fileLock = new();

    public PersistentLlmTelemetry(ILlmTelemetry inner, IConfiguration config, ILogger<PersistentLlmTelemetry> logger)
    {
        _inner = inner;
        _logger = logger;
        var dir = config["Cache:DiskCachePath"] ?? "cache";
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "llm-telemetry.jsonl");
        Replay();
    }

    public void Record(LlmCallRecord record)
    {
        _inner.Record(record);
        try
        {
            lock (_fileLock)
                File.AppendAllText(_path, JsonSerializer.Serialize(record, _json) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Telemetry] Failed to persist call record — continuing in-memory only.");
        }
    }

    public LlmTelemetrySnapshot Snapshot() => _inner.Snapshot();

    public double EstimateCostUsd(string provider, int inputTokens, int outputTokens, int cachedInputTokens = 0)
        => _inner.EstimateCostUsd(provider, inputTokens, outputTokens, cachedInputTokens);

    private void Replay()
    {
        if (!File.Exists(_path)) return;
        var count = 0;
        try
        {
            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var record = JsonSerializer.Deserialize<LlmCallRecord>(line, _json);
                    if (record is not null) { _inner.Record(record); count++; }
                }
                catch (JsonException) { /* skip a corrupt line */ }
            }
            _logger.LogInformation("[Telemetry] Replayed {Count} persisted call record(s) from {Path}.", count, _path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Telemetry] Failed to replay persisted telemetry.");
        }
    }
}
