namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// Trust/provenance metadata attached to a generated output so downstream consumers
/// (comparison, white papers, analysts) can judge how much to rely on it. All fields are
/// derived from data the pipeline already produces — no extra LLM call.
/// </summary>
public sealed record OutputProvenance
{
    /// <summary>The model/engine that produced the result (mirrors the result's ModelUsed).</summary>
    public required string ModelUsed { get; init; }

    /// <summary>Providers attempted, in order, before one succeeded (or the heuristic engine ran).</summary>
    public string[] ProvidersAttempted { get; init; } = [];

    /// <summary>Live web sources queried for grounding, if any.</summary>
    public string[] LiveSourcesQueried { get; init; } = [];

    public int SourceCount { get; init; }

    /// <summary>True when a live model produced the output AND it passed a fact/goal check.</summary>
    public bool FactChecked { get; init; }

    /// <summary>Heuristic confidence 0.0–1.0 — a deterministic function of the signals below.</summary>
    public double Confidence { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    private const string HeuristicEngine = "Heuristic Engine (Offline)";

    /// <summary>
    /// Builds provenance with a deterministic confidence estimate:
    ///  • base 0.55 for a live model, 0.30 for the offline heuristic engine;
    ///  • +0.20 when fact-checked; +up to 0.15 for grounding sources; +0.05 for a first-attempt success.
    /// Clamped to [0,1].
    /// </summary>
    public static OutputProvenance From(
        string modelUsed,
        IReadOnlyList<string> providersAttempted,
        IReadOnlyList<string>? liveSources = null,
        bool factChecked = false)
    {
        var sources = liveSources ?? [];
        var isLive = !modelUsed.Contains(HeuristicEngine, StringComparison.Ordinal);

        var confidence = isLive ? 0.55 : 0.30;
        if (factChecked) confidence += 0.20;
        confidence += Math.Min(0.15, sources.Count * 0.03);
        if (isLive && providersAttempted.Count <= 1) confidence += 0.05;
        confidence = Math.Clamp(confidence, 0.0, 1.0);

        return new OutputProvenance
        {
            ModelUsed = modelUsed,
            ProvidersAttempted = [.. providersAttempted],
            LiveSourcesQueried = [.. sources],
            SourceCount = sources.Count,
            FactChecked = factChecked,
            Confidence = Math.Round(confidence, 2)
        };
    }
}
