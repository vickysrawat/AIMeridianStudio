using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MeridianStudio.API.Infrastructure.Diagnostics;

public sealed record LearnedMermaidFix(
    string SourceHash,
    string Repaired,
    string? ErrorSignature,
    string SourcePreview,
    string CreatedAt);

public sealed record MermaidUnresolved(
    string SourcePreview,
    string? Error,
    string? ErrorSignature,
    string CreatedAt);

public interface ILearnedMermaidFixStore
{
    /// <summary>Returns a previously-learned repaired diagram for this exact source, or null.</summary>
    string? TryGet(string source);

    /// <summary>Persists a verified repair so the identical broken source is fixed deterministically next time.</summary>
    void Record(string source, string repaired, string? errorSignature);

    IReadOnlyList<LearnedMermaidFix> Recent(int take);

    /// <summary>Records a diagram that no deterministic rule (nor the LLM tier) could fix — for review/promotion.</summary>
    void RecordUnresolved(string source, string? error, string? errorSignature);

    IReadOnlyList<MermaidUnresolved> RecentUnresolved(int take);
}

/// <summary>
/// Append-only JSONL store of verified LLM-tier repairs, keyed by a hash of the normalized source.
/// A hit short-circuits the tiered flow with zero LLM. Mirrors PersistentLlmTelemetry (lock + append +
/// replay on startup). Register as a singleton.
/// </summary>
public sealed class LearnedMermaidFixStore : ILearnedMermaidFixStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly ConcurrentDictionary<string, string> _byHash = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<LearnedMermaidFix> _recent = new();
    private readonly ConcurrentQueue<MermaidUnresolved> _unresolved = new();
    private readonly string _path;
    private readonly string _unresolvedPath;
    private readonly object _fileLock = new();
    private readonly ILogger<LearnedMermaidFixStore> _logger;

    public LearnedMermaidFixStore(IConfiguration config, ILogger<LearnedMermaidFixStore> logger)
    {
        _logger = logger;
        var dir = config["Cache:DiskCachePath"] ?? "cache";
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "learned-mermaid-fixes.jsonl");
        _unresolvedPath = Path.Combine(dir, "mermaid-unresolved.jsonl");
        Replay();
    }

    public void RecordUnresolved(string source, string? error, string? errorSignature)
    {
        var entry = new MermaidUnresolved(
            source.Length > 400 ? source[..400] : source,
            error, errorSignature, DateTimeOffset.UtcNow.ToString("O"));
        _unresolved.Enqueue(entry);
        while (_unresolved.Count > 200) _unresolved.TryDequeue(out _);
        try
        {
            lock (_fileLock)
                File.AppendAllText(_unresolvedPath, JsonSerializer.Serialize(entry, Json) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LearnedFix] Failed to persist unresolved sample — kept in memory only.");
        }
    }

    public IReadOnlyList<MermaidUnresolved> RecentUnresolved(int take)
        => [.. _unresolved.Reverse().Take(Math.Clamp(take, 1, 200))];

    public string? TryGet(string source)
        => _byHash.TryGetValue(Hash(source), out var repaired) ? repaired : null;

    public void Record(string source, string repaired, string? errorSignature)
    {
        var entry = new LearnedMermaidFix(
            Hash(source), repaired, errorSignature,
            source.Length > 200 ? source[..200] : source,
            DateTimeOffset.UtcNow.ToString("O"));

        _byHash[entry.SourceHash] = repaired;
        _recent.Enqueue(entry);
        while (_recent.Count > 200) _recent.TryDequeue(out _);

        try
        {
            lock (_fileLock)
                File.AppendAllText(_path, JsonSerializer.Serialize(entry, Json) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LearnedFix] Failed to persist — kept in memory only.");
        }
    }

    public IReadOnlyList<LearnedMermaidFix> Recent(int take)
        => [.. _recent.Reverse().Take(Math.Clamp(take, 1, 200))];

    private static string Hash(string source)
    {
        var normalized = source.Replace("\r\n", "\n").Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }

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
                    var e = JsonSerializer.Deserialize<LearnedMermaidFix>(line, Json);
                    if (e is not null) { _byHash[e.SourceHash] = e.Repaired; _recent.Enqueue(e); count++; }
                }
                catch (JsonException) { /* skip corrupt line */ }
            }
            while (_recent.Count > 200) _recent.TryDequeue(out _);
            _logger.LogInformation("[LearnedFix] Replayed {Count} learned Mermaid fix(es).", count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LearnedFix] Replay failed.");
        }
    }
}
