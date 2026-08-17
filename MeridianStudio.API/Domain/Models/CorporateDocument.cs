namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// Compiled legal or executive document generated from a SystemBlueprint.
/// </summary>
public sealed record CorporateDocument
{
    public required string Id { get; init; }
    public required string BlueprintId { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required string TemplateType { get; init; }
    public required string CreatedAt { get; init; }
    public string ModelUsed { get; init; } = string.Empty;

    // ── Goal-directed generation fields ──────────────────────────────────────
    /// <summary>0–100 percentage of goal criteria met.</summary>
    public int GoalAchievementPct { get; init; }
    /// <summary>True when GoalAchievementPct >= 80.</summary>
    public bool GoalAchieved { get; init; }
    /// <summary>Number of LLM passes used (1–3).</summary>
    public int IterationsUsed { get; init; }
    /// <summary>Criteria that passed the judge evaluation.</summary>
    public string[] PassedCriteria { get; init; } = [];
    /// <summary>Criteria that failed; empty when goal fully achieved.</summary>
    public string[] FailedCriteria { get; init; } = [];
    /// <summary>Per-criterion explanation of why it failed the judge evaluation.</summary>
    public Dictionary<string, string> FailureReasons { get; init; } = [];
    /// <summary>Per-criterion 0-100 score (A1) — makes documents machine-comparable across runs.</summary>
    public Dictionary<string, int> CriterionScores { get; init; } = [];
    /// <summary>The goal string that drove generation (user-selected or refined).</summary>
    public string EffectiveGoal { get; init; } = string.Empty;
    /// <summary>The criteria list used during generation.</summary>
    public string[] EffectiveCriteria { get; init; } = [];
    /// <summary>True when the user edited a suggestion before generating.</summary>
    public bool WasRefined { get; init; }
    /// <summary>
    /// True only when a live model produced the document AND it passed the goal/faithfulness
    /// judge. False for heuristic-engine (offline) output — which the judge auto-passes without
    /// evaluation — and for single-pass legacy output. Lets the UI honestly label unverified docs.
    /// </summary>
    public bool FactChecked { get; init; }

    // ── Structured-native (the document IS structure; Content is the render) ─────────────────
    /// <summary>Stable document id used to target by-id fixes.</summary>
    public string DocumentId { get; init; } = string.Empty;
    /// <summary>The canonical structured document (sections + criteria stack + sources). The client
    /// echoes this back on a Fix; the UI reads its criteria for the scorecard. Content is its render.</summary>
    public StructuredDocument? Structured { get; init; }

    /// <summary>Trust/provenance metadata (model, providers tried, sources, fact-check, confidence). Null on legacy paths.</summary>
    public OutputProvenance? Provenance { get; init; }

    /// <summary>
    /// Fingerprint of the grounding blueprint at generation time. GET /api/artifacts/{id}/freshness compares
    /// it to the current blueprint's fingerprint to flag the document stale after a blueprint revision.
    /// Empty on legacy/assessment-only docs → "unknown freshness" (no false stale).
    /// </summary>
    public string GroundedBlueprintFingerprint { get; init; } = string.Empty;

    public static CorporateDocument Create(
        string id,
        string blueprintId,
        string title,
        string content,
        string templateType,
        string? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId, nameof(blueprintId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title, nameof(title));
        ArgumentException.ThrowIfNullOrWhiteSpace(content, nameof(content));
        ArgumentException.ThrowIfNullOrWhiteSpace(templateType, nameof(templateType));

        return new CorporateDocument
        {
            Id = id,
            BlueprintId = blueprintId,
            Title = title,
            Content = content,
            TemplateType = templateType,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow.ToString("O")
        };
    }
}
