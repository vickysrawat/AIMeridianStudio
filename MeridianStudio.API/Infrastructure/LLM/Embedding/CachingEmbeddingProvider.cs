using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace MeridianStudio.API.Infrastructure.LLM.Embedding;

/// <summary>
/// Decorates an <see cref="IEmbeddingProvider"/> with an in-memory cache so repeated texts skip
/// the (network) call — cutting latency and exposure to transient upstream failures. Only
/// successful results are cached; exceptions propagate unchanged so callers keep their retry and
/// fallback behaviour. Cache keys are namespaced by the inner provider's <see cref="SpaceId"/>,
/// so vectors from different embedding spaces can never collide (they are not comparable).
/// </summary>
public sealed class CachingEmbeddingProvider(IEmbeddingProvider inner, IMemoryCache cache) : IEmbeddingProvider
{
    // Slow-moving text → vector mapping; a sliding window keeps hot entries warm without unbounded growth.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    public string SpaceId => inner.SpaceId;
    public bool IsRealModel => inner.IsRealModel;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var key = CacheKey(text);
        if (cache.TryGetValue(key, out float[]? cached) && cached is not null)
            return cached;

        var vector = await inner.EmbedAsync(text, ct);
        Store(key, vector);
        return vector;
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        var result   = new float[texts.Count][];
        var missIdx   = new List<int>();
        var missTexts = new List<string>();

        for (var i = 0; i < texts.Count; i++)
        {
            if (cache.TryGetValue(CacheKey(texts[i]), out float[]? cached) && cached is not null)
                result[i] = cached;
            else
            {
                missIdx.Add(i);
                missTexts.Add(texts[i]);
            }
        }

        if (missTexts.Count > 0)
        {
            var embedded = await inner.EmbedBatchAsync(missTexts, ct);
            for (var k = 0; k < missIdx.Count; k++)
            {
                var vector = embedded[k];
                result[missIdx[k]] = vector;
                Store(CacheKey(missTexts[k]), vector);
            }
        }

        return result;
    }

    private void Store(string key, float[] vector) =>
        cache.Set(key, vector, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = Ttl });

    // Namespaced by SpaceId so vectors from different spaces never collide; SHA-256 keeps the key
    // compact and collision-resistant regardless of text length.
    private string CacheKey(string text)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)));
        return $"embed:{inner.SpaceId}:{hash}";
    }
}
