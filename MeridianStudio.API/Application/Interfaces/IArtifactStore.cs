using MeridianStudio.API.Domain.Artifacts;

namespace MeridianStudio.API.Application.Interfaces;

/// <summary>
/// Durable, append-only, tenant-scoped store for generated artifacts. Implementations:
/// SQLite (<c>EfArtifactStore</c>, default), disk (<c>DiskArtifactStore</c>, fallback),
/// and optionally SQL Server / PostgreSQL — all swappable behind this seam because the
/// payload is stored as a raw JSON element.
///
/// Contract notes:
///  • <see cref="SaveAsync"/> is append-only and dedup-aware — see its docs.
///  • Read methods take an explicit <c>tenantId</c>; a store must never return another
///    tenant's rows. Callers pass the resolved tenant (real principal or dev fallback).
/// </summary>
public interface IArtifactStore
{
    /// <summary>
    /// Persists <paramref name="payload"/> as a new version of its lineage.
    /// <para>
    /// Dedup: if the latest version of <c>meta.LineageId</c> (within the tenant) already has
    /// an identical <c>meta.RequestHash</c>, no new row is written and the existing artifact
    /// is returned. Otherwise a new version is assigned (<c>max(version)+1</c>) atomically and
    /// inserted. Never mutates prior versions.
    /// </para>
    /// </summary>
    Task<StoredArtifact> SaveAsync<T>(T payload, ArtifactMetadata meta, CancellationToken ct = default);

    /// <summary>Returns the artifact by id within the tenant, or null if absent.</summary>
    Task<StoredArtifact?> GetAsync(string artifactId, string tenantId, CancellationToken ct = default);

    /// <summary>Convenience: get and deserialise the payload to <typeparamref name="T"/>.</summary>
    Task<T?> GetPayloadAsync<T>(string artifactId, string tenantId, CancellationToken ct = default) where T : class;

    /// <summary>Metadata-only list/filter (cheap — no payload deserialisation).</summary>
    Task<IReadOnlyList<ArtifactMetadata>> QueryAsync(ArtifactQuery query, string tenantId, CancellationToken ct = default);

    /// <summary>All versions of a lineage, newest first.</summary>
    Task<IReadOnlyList<ArtifactMetadata>> GetVersionsAsync(string lineageId, string tenantId, CancellationToken ct = default);

    /// <summary>Bulk fetch (for comparison / white-paper assembly). Missing/foreign ids are skipped.</summary>
    Task<IReadOnlyList<StoredArtifact>> GetManyAsync(IEnumerable<string> artifactIds, string tenantId, CancellationToken ct = default);

    /// <summary>Deletes a single version. Returns true if a row was removed.</summary>
    Task<bool> DeleteAsync(string artifactId, string tenantId, CancellationToken ct = default);

    /// <summary>Deletes every version of a lineage. Returns the number removed.</summary>
    Task<int> DeleteLineageAsync(string lineageId, string tenantId, CancellationToken ct = default);

    /// <summary>Retention purge: removes all artifacts created before <paramref name="cutoff"/> (all tenants). Returns count removed.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
