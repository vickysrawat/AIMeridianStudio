namespace MeridianStudio.API.Infrastructure.LLM.Embedding;

/// <summary>
/// Offline, network-free embedding fallback: a deterministic hashed bag-of-words vector
/// (the "hashing trick"). Tokens are folded into a fixed number of buckets via a stable
/// FNV-1a hash and the vector is L2-normalised, so cosine similarity approximates lexical
/// overlap. Deterministic across restarts — vectors persisted to disk stay comparable.
/// Always usable; selected when no embedding API key is configured.
/// </summary>
public sealed class LexicalEmbeddingProvider : IEmbeddingProvider
{
    private const int Dim = 256;

    public string SpaceId => $"lexical:v1-{Dim}";
    public bool IsRealModel => false;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(Embed(text));

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(Embed)]);

    private static float[] Embed(string text)
    {
        var vec = new float[Dim];
        if (string.IsNullOrWhiteSpace(text)) return vec;

        foreach (var token in Tokenize(text))
        {
            var bucket = (int)(Fnv1a(token) % Dim);
            vec[bucket] += 1f;
        }

        // L2-normalise so cosine similarity is well defined.
        double sumSq = 0;
        foreach (var v in vec) sumSq += v * (double)v;
        if (sumSq > 0)
        {
            var inv = (float)(1.0 / Math.Sqrt(sumSq));
            for (var i = 0; i < vec.Length; i++) vec[i] *= inv;
        }

        return vec;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var current = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
            }
            else if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
        }
        if (current.Length > 0) yield return current.ToString();
    }

    // Stable hash (unlike string.GetHashCode, which is randomised per process).
    private static uint Fnv1a(string s)
    {
        uint h = 2166136261;
        foreach (var c in s)
        {
            h ^= c;
            h *= 16777619;
        }
        return h;
    }
}
