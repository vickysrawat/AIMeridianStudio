namespace MeridianStudio.API.Infrastructure.LLM.Embedding;

/// <summary>
/// Produces embedding vectors for semantic retrieval. A single implementation is chosen at
/// startup (Gemini when an API key is present, otherwise the deterministic lexical fallback)
/// so all vectors in a process share one space. Vectors from different <see cref="SpaceId"/>
/// values are NOT comparable — callers must re-embed when a stored vector's space differs.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Stable id of the vector space (e.g. "gemini:text-embedding-004", "lexical:v1-256").</summary>
    string SpaceId { get; }

    /// <summary>True when this is a real hosted model (vs the offline lexical fallback).</summary>
    bool IsRealModel { get; }

    /// <summary>Embeds a single text. Throws on transport/model failure so callers can fall back.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>Embeds many texts, preserving order. Throws on failure.</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
