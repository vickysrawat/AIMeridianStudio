using System.Text.Json;

namespace MeridianStudio.API.Domain.Artifacts;

/// <summary>
/// A persisted artifact: its metadata plus the serialised domain record as a raw
/// <see cref="JsonElement"/>. Keeping the payload type-erased lets one store hold all
/// five sealed record types without a discriminated hierarchy — mirrors the
/// <c>DiskEnvelope</c> pattern in <c>PayloadCache</c>.
/// </summary>
public sealed record StoredArtifact
{
    public required ArtifactMetadata Metadata { get; init; }

    public required JsonElement Payload { get; init; }
}
