namespace MeridianStudio.API.Domain.Artifacts;

/// <summary>
/// Metadata-only filter for listing artifacts. Never deserialises payloads, so list /
/// filter / analytics stay cheap. All filters are AND-combined; null filters are ignored.
/// </summary>
public sealed record ArtifactQuery
{
    public ArtifactKind? Kind { get; init; }
    public string? Domain { get; init; }
    public string? SubDomain { get; init; }
    public string? LineageId { get; init; }
    public DateTimeOffset? CreatedAfter { get; init; }
    public DateTimeOffset? CreatedBefore { get; init; }

    /// <summary>When true, returns only the highest version per lineage.</summary>
    public bool LatestVersionOnly { get; init; } = true;

    public int Skip { get; init; }
    public int Take { get; init; } = 50;
}
