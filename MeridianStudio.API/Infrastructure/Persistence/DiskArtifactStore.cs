using System.Collections.Concurrent;
using System.Text.Json;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Artifacts;

namespace MeridianStudio.API.Infrastructure.Persistence;

/// <summary>
/// Dependency-free durable artifact store: one JSON file per artifact under
/// <c>{Persistence:Path}/artifacts/</c> plus an in-memory metadata index rebuilt from disk
/// on first use. Serves as the no-dependency fallback provider and the on-disk backup format.
///
/// Concurrency: a single <see cref="SemaphoreSlim"/> serialises writes (append-only, so the
/// version race #3 cannot occur within a process); artifact files are written atomically via
/// temp-file + move. Single-process only — documented in the plan. Scales to low thousands of
/// artifacts before the in-memory index becomes the bottleneck.
/// </summary>
public sealed class DiskArtifactStore : IArtifactStore
{
    private readonly string _root;
    private readonly ILogger<DiskArtifactStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Metadata index: artifactId -> metadata. Loaded lazily from disk, then authoritative.
    private readonly ConcurrentDictionary<string, ArtifactMetadata> _index
        = new(StringComparer.Ordinal);
    private volatile bool _loaded;
    private readonly object _loadGate = new();

    public DiskArtifactStore(IConfiguration config, ILogger<DiskArtifactStore> logger)
    {
        _logger = logger;
        var basePath = config["Persistence:Path"] ?? "artifacts";
        _root = Path.Combine(basePath, "artifacts");
        Directory.CreateDirectory(_root);
    }

    // ── Save (append-only, dedup-aware, version-assigning) ─────────────────────

    public async Task<StoredArtifact> SaveAsync<T>(T payload, ArtifactMetadata meta, CancellationToken ct = default)
    {
        EnsureLoaded();
        await _writeLock.WaitAsync(ct);
        try
        {
            // Latest version of this lineage within the tenant.
            var latest = _index.Values
                .Where(m => m.TenantId == meta.TenantId && m.LineageId == meta.LineageId)
                .OrderByDescending(m => m.Version)
                .FirstOrDefault();

            // Dedup: identical request for the same lineage → return the existing artifact.
            if (latest is not null && latest.RequestHash == meta.RequestHash)
            {
                var existing = await ReadFileAsync(latest.ArtifactId, ct);
                if (existing is not null)
                {
                    _logger.LogInformation(
                        "[Artifacts] Dedup hit — lineage {Lineage} v{Version} reused (hash match).",
                        meta.LineageId, latest.Version);
                    return existing;
                }
            }

            var version = (latest?.Version ?? 0) + 1;
            var finalMeta = meta with { Version = version };
            var artifact = new StoredArtifact
            {
                Metadata = finalMeta,
                Payload = JsonSerializer.SerializeToElement(payload, ArtifactSerialization.Options)
            };

            await WriteFileAsync(artifact, ct);
            _index[finalMeta.ArtifactId] = finalMeta;
            return artifact;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ── Reads (tenant-scoped) ──────────────────────────────────────────────────

    public async Task<StoredArtifact?> GetAsync(string artifactId, string tenantId, CancellationToken ct = default)
    {
        EnsureLoaded();
        if (!_index.TryGetValue(artifactId, out var meta) || meta.TenantId != tenantId)
            return null;
        return await ReadFileAsync(artifactId, ct);
    }

    public async Task<T?> GetPayloadAsync<T>(string artifactId, string tenantId, CancellationToken ct = default) where T : class
    {
        var stored = await GetAsync(artifactId, tenantId, ct);
        return stored?.Payload.Deserialize<T>(ArtifactSerialization.Options);
    }

    public Task<IReadOnlyList<ArtifactMetadata>> QueryAsync(ArtifactQuery query, string tenantId, CancellationToken ct = default)
    {
        EnsureLoaded();
        IEnumerable<ArtifactMetadata> rows = _index.Values.Where(m => m.TenantId == tenantId);

        if (query.Kind is { } kind) rows = rows.Where(m => m.Kind == kind);
        if (!string.IsNullOrWhiteSpace(query.Domain))
            rows = rows.Where(m => string.Equals(m.Domain, query.Domain, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.SubDomain))
            rows = rows.Where(m => string.Equals(m.SubDomain, query.SubDomain, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.LineageId))
            rows = rows.Where(m => m.LineageId == query.LineageId);
        if (query.CreatedAfter is { } after) rows = rows.Where(m => m.CreatedAt >= after);
        if (query.CreatedBefore is { } before) rows = rows.Where(m => m.CreatedAt <= before);

        if (query.LatestVersionOnly)
            rows = rows.GroupBy(m => m.LineageId).Select(g => g.MaxBy(m => m.Version)!);

        var result = rows
            .OrderByDescending(m => m.CreatedAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToList();

        return Task.FromResult<IReadOnlyList<ArtifactMetadata>>(result);
    }

    public Task<IReadOnlyList<ArtifactMetadata>> GetVersionsAsync(string lineageId, string tenantId, CancellationToken ct = default)
    {
        EnsureLoaded();
        var result = _index.Values
            .Where(m => m.TenantId == tenantId && m.LineageId == lineageId)
            .OrderByDescending(m => m.Version)
            .ToList();
        return Task.FromResult<IReadOnlyList<ArtifactMetadata>>(result);
    }

    public async Task<IReadOnlyList<StoredArtifact>> GetManyAsync(IEnumerable<string> artifactIds, string tenantId, CancellationToken ct = default)
    {
        var result = new List<StoredArtifact>();
        foreach (var id in artifactIds)
        {
            var stored = await GetAsync(id, tenantId, ct);
            if (stored is not null) result.Add(stored);
        }
        return result;
    }

    // ── Delete / retention ─────────────────────────────────────────────────────

    public async Task<bool> DeleteAsync(string artifactId, string tenantId, CancellationToken ct = default)
    {
        EnsureLoaded();
        await _writeLock.WaitAsync(ct);
        try
        {
            if (!_index.TryGetValue(artifactId, out var meta) || meta.TenantId != tenantId)
                return false;
            _index.TryRemove(artifactId, out _);
            TryDeleteFile(artifactId);
            return true;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<int> DeleteLineageAsync(string lineageId, string tenantId, CancellationToken ct = default)
    {
        EnsureLoaded();
        await _writeLock.WaitAsync(ct);
        try
        {
            var victims = _index.Values
                .Where(m => m.TenantId == tenantId && m.LineageId == lineageId)
                .Select(m => m.ArtifactId)
                .ToList();
            foreach (var id in victims)
            {
                _index.TryRemove(id, out _);
                TryDeleteFile(id);
            }
            return victims.Count;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        EnsureLoaded();
        await _writeLock.WaitAsync(ct);
        try
        {
            var victims = _index.Values
                .Where(m => m.CreatedAt < cutoff)
                .Select(m => m.ArtifactId)
                .ToList();
            foreach (var id in victims)
            {
                _index.TryRemove(id, out _);
                TryDeleteFile(id);
            }
            if (victims.Count > 0)
                _logger.LogInformation("[Artifacts] Retention purge removed {Count} artifact(s).", victims.Count);
            return victims.Count;
        }
        finally { _writeLock.Release(); }
    }

    // ── Disk helpers ─────────────────────────────────────────────────────────

    private string FilePath(string artifactId) => Path.Combine(_root, artifactId + ".json");

    private async Task WriteFileAsync(StoredArtifact artifact, CancellationToken ct)
    {
        var path = FilePath(artifact.Metadata.ArtifactId);
        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(artifact, ArtifactSerialization.Options);
        await File.WriteAllTextAsync(tmp, json, ct);
        File.Move(tmp, path, overwrite: true); // atomic replace
    }

    private async Task<StoredArtifact?> ReadFileAsync(string artifactId, CancellationToken ct)
    {
        var path = FilePath(artifactId);
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<StoredArtifact>(json, ArtifactSerialization.Options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Artifacts] Failed to read artifact {Id} from disk.", artifactId);
            return null;
        }
    }

    private void TryDeleteFile(string artifactId)
    {
        try { var p = FilePath(artifactId); if (File.Exists(p)) File.Delete(p); }
        catch { /* ignore */ }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_loadGate)
        {
            if (_loaded) return;
            foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var artifact = JsonSerializer.Deserialize<StoredArtifact>(json, ArtifactSerialization.Options);
                    if (artifact is not null)
                        _index[artifact.Metadata.ArtifactId] = artifact.Metadata;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Artifacts] Skipping unreadable index entry {File}.", file);
                }
            }
            _loaded = true;
            _logger.LogInformation("[Artifacts] Disk store loaded {Count} artifact(s) from {Root}.", _index.Count, _root);
        }
    }
}
