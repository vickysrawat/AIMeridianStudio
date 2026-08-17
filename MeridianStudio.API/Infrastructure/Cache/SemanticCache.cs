using System.Collections.Concurrent;
using MeridianStudio.API.Infrastructure.LLM.Embedding;
using MeridianStudio.API.Infrastructure.Retrieval;

namespace MeridianStudio.API.Infrastructure.Cache;

/// <summary>
/// Opt-in semantic cache (B3). On an exact-hash miss, maps a near-duplicate request (by embedding
/// cosine similarity ≥ threshold) to the exact cache key of a prior equivalent request, so
/// <see cref="PayloadCache"/> can serve it. Default OFF (<c>Cache:Semantic:Enabled</c>); when off,
/// <see cref="ResolveKey"/> is a no-op returning null. Holds a bounded in-memory ring of recent
/// (embedding, exactKey) pairs — lost on restart, which is fine for a hit-rate optimization.
/// Register as singleton.
/// </summary>
public sealed class SemanticCache(
    IEmbeddingProvider embedder,
    IConfiguration config,
    ILogger<SemanticCache> logger)
{
    private const int Capacity = 500;
    private readonly ConcurrentQueue<(float[] Vector, string ExactKey, string Space)> _entries = new();

    private bool Enabled => config.GetValue("Cache:Semantic:Enabled", false);
    private double Threshold => config.GetValue("Cache:Semantic:Threshold", 0.95);

    /// <summary>
    /// Returns the exact cache key of a semantically-equivalent prior request, or null if disabled,
    /// no embedding available, or nothing clears the similarity threshold.
    /// </summary>
    public async Task<string?> ResolveKeyAsync(string queryText, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(queryText)) return null;

        try
        {
            var vec = await embedder.EmbedAsync(queryText, ct);
            var space = embedder.SpaceId;
            var threshold = Threshold;

            string? bestKey = null;
            var bestScore = threshold;
            foreach (var (vector, exactKey, entrySpace) in _entries)
            {
                if (entrySpace != space) continue; // only compare within the same embedding space
                var score = VectorMath.Cosine(vec, vector);
                if (score >= bestScore) { bestScore = score; bestKey = exactKey; }
            }

            if (bestKey is not null)
                logger.LogInformation("[SemanticCache] Hit — cosine {Score:F3} ≥ {T:F2}.", bestScore, threshold);
            return bestKey;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SemanticCache] Resolve failed — treating as miss.");
            return null;
        }
    }

    /// <summary>Records the embedding of a freshly-computed request against its exact cache key.</summary>
    public async Task RememberAsync(string queryText, string exactKey, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(queryText)) return;
        try
        {
            var vec = await embedder.EmbedAsync(queryText, ct);
            _entries.Enqueue((vec, exactKey, embedder.SpaceId));
            while (_entries.Count > Capacity) _entries.TryDequeue(out _);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SemanticCache] Remember failed — skipping.");
        }
    }
}
