namespace MeridianStudio.API.Domain.Artifacts;

/// <summary>
/// Uniform, storage-agnostic envelope describing one persisted artifact version.
/// Kept deliberately flat so every store (SQLite, disk, SQL) can map it to indexed
/// columns without a per-kind hierarchy — the kind-specific payload lives in
/// <see cref="StoredArtifact.Payload"/> as a raw <c>JsonElement</c>.
/// </summary>
public sealed record ArtifactMetadata
{
    /// <summary>Unique id for this specific version (ULID-style, monotonic-ish).</summary>
    public required string ArtifactId { get; init; }

    public required ArtifactKind Kind { get; init; }

    public string? Domain { get; init; }
    public string? SubDomain { get; init; }

    /// <summary>Human-readable label (solution name, document title, keywords, …).</summary>
    public string? Title { get; init; }

    /// <summary>Which model/engine produced the payload (mirrors the payload's own ModelUsed).</summary>
    public string ModelUsed { get; init; } = "";

    /// <summary>
    /// SHA-256 of the originating request (from <c>PayloadCache.ComputeKey</c>). Used to
    /// dedup: an identical request for the same lineage does not mint a new version.
    /// </summary>
    public required string RequestHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>1-based version within the lineage. Assigned by the store on save.</summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// Stable key grouping all versions of the same logical artifact (e.g. a blueprint's
    /// SolutionId, a research run's normalized domain+subdomain+weights).
    /// </summary>
    public required string LineageId { get; init; }

    /// <summary>Upstream artifact this was derived from (e.g. Document → Blueprint).</summary>
    public string? ParentArtifactId { get; init; }

    /// <summary>Payload shape version so old rows deserialize safely after model changes.</summary>
    public int SchemaVersion { get; init; } = 1;

    public string[] Tags { get; init; } = [];

    // ── Tenancy / governance (Track 1f) ───────────────────────────────────────
    // Populated from the authenticated principal when Auth:Enabled, otherwise the
    // configured Auth:DevTenantId fallback so tenant-scoping paths run in dev too.
    public string TenantId { get; init; } = "";
    public string? CreatedBy { get; init; }
}
