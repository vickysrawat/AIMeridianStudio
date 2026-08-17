namespace MeridianStudio.API.Infrastructure.Retrieval;

/// <summary>Vector helpers for semantic ranking.</summary>
public static class VectorMath
{
    /// <summary>
    /// Cosine similarity in [-1, 1]. Returns 0 when either vector is null/empty, the lengths
    /// differ (different embedding spaces), or either has zero magnitude.
    /// </summary>
    public static double Cosine(float[]? a, float[]? b)
    {
        if (a is null || b is null || a.Length == 0 || a.Length != b.Length) return 0;

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            na  += a[i] * (double)a[i];
            nb  += b[i] * (double)b[i];
        }

        if (na <= 0 || nb <= 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
