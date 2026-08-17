using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MeridianStudio.API.Infrastructure.Cache;

/// <summary>
/// Thread-safe, TTL-aware cache with two layers:
///   1. In-memory ConcurrentDictionary (fast path, lost on restart)
///   2. Disk JSON files (survives restarts, loaded on memory miss)
///
/// Configuration keys (appsettings):
///   Cache:DiskCachePath     — relative or absolute path for JSON files (default: "cache")
///   Cache:DefaultTtlHours   — fallback TTL when services don't specify one (default: 1h)
///
/// Register as Singleton.
/// </summary>
public sealed class PayloadCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _store
        = new(StringComparer.Ordinal);

    private readonly TimeSpan _defaultTtl;
    private readonly string   _diskPath;

    private static readonly JsonSerializerOptions _jsonOpts
        = new() { WriteIndented = false };

    public PayloadCache(IConfiguration config)
    {
        _defaultTtl = TimeSpan.FromHours(
            config.GetValue<double>("Cache:DefaultTtlHours", 1.0));

        _diskPath = config["Cache:DiskCachePath"] ?? "cache";
        Directory.CreateDirectory(_diskPath);
    }

    // ── Key computation ───────────────────────────────────────────────────────

    /// <summary>
    /// SHA-256 hash of the JSON-serialised payload — stable across calls
    /// for the same object graph.
    /// </summary>
    public string ComputeKey<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public bool TryGet<T>(string key, [NotNullWhen(true)] out T? value) where T : class
    {
        // Layer 1: memory
        if (_store.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            value = entry.Value as T;
            return value is not null;
        }
        _store.TryRemove(key, out _);

        // Layer 2: disk
        if (TryReadFromDisk<T>(key, out var diskValue, out var expiresAt))
        {
            _store[key] = new CacheEntry(diskValue, expiresAt);
            value = diskValue;
            return true;
        }

        value = null;
        return false;
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>Store using the default configured TTL.</summary>
    public void Set<T>(string key, T value) where T : class
        => Set(key, value, _defaultTtl);

    /// <summary>Store using an explicit TTL (e.g. per-operation configured value).</summary>
    public void Set<T>(string key, T value, TimeSpan ttl) where T : class
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
        _store[key] = new CacheEntry(value, expiresAt);
        WriteToDisk(key, value, expiresAt);
    }

    // ── Evict ─────────────────────────────────────────────────────────────────

    public void Evict(string key)
    {
        _store.TryRemove(key, out _);
        DeleteFromDisk(key);
    }

    // ── Disk helpers ──────────────────────────────────────────────────────────

    private string DiskFilePath(string key)
        => Path.Combine(_diskPath, key + ".json");

    private bool TryReadFromDisk<T>(
        string key,
        [NotNullWhen(true)] out T? value,
        out DateTimeOffset expiresAt) where T : class
    {
        value     = null;
        expiresAt = default;
        var path  = DiskFilePath(key);

        if (!File.Exists(path)) return false;

        bool expired = false;
        try
        {
            var json  = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root  = doc.RootElement;

            expiresAt = root.GetProperty("expiresAt").GetDateTimeOffset();

            if (DateTimeOffset.UtcNow > expiresAt)
            {
                expired = true;
                return false;
            }

            value = root.GetProperty("data").Deserialize<T>();
            return value is not null;
        }
        catch
        {
            // Corrupt or unreadable — delete to avoid repeated failures
            TryDeleteFile(path);
            return false;
        }
        finally
        {
            if (expired) TryDeleteFile(path);
        }
    }

    private void WriteToDisk<T>(string key, T value, DateTimeOffset expiresAt)
    {
        try
        {
            var envelope = new DiskEnvelope(expiresAt, JsonSerializer.SerializeToElement(value));
            var json     = JsonSerializer.Serialize(envelope, _jsonOpts);
            File.WriteAllText(DiskFilePath(key), json);
        }
        catch { /* disk write failure — degrade gracefully to memory-only */ }
    }

    private void DeleteFromDisk(string key) => TryDeleteFile(DiskFilePath(key));

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* ignore */ }
    }

    // ── Internal types ────────────────────────────────────────────────────────

    private sealed record CacheEntry(object Value, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    }

    private sealed record DiskEnvelope(DateTimeOffset ExpiresAt, JsonElement Data);
}
