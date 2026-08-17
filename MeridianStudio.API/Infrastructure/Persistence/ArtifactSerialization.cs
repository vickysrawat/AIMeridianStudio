using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeridianStudio.API.Infrastructure.Persistence;

/// <summary>
/// Shared JSON options and id generation for the artifact store, kept consistent with the
/// API's global camelCase + string-enum conventions.
/// </summary>
public static class ArtifactSerialization
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Time-ordered, collision-resistant id: fixed-width unix-ms prefix (lexicographically
    /// sortable by creation time) + a random suffix. A dependency-free ULID substitute.
    /// </summary>
    public static string NewArtifactId()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x11")
         + Guid.NewGuid().ToString("N")[..13];
}
