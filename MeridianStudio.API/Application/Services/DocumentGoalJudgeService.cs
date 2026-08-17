using System.Text.Json;
using MeridianStudio.API.Infrastructure.LLM;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Evaluates a generated document against the user's selected goal and criteria.
/// Returns a GoalEvaluation that drives the iteration loop in DocumentService.
/// Skips evaluation if the Heuristic Engine produced the document.
/// </summary>
public sealed class DocumentGoalJudgeService(
    LLMOrchestrator orchestrator,
    ILogger<DocumentGoalJudgeService> logger)
{
    public sealed record GoalEvaluation
    {
        public required int GoalAchievementPct { get; init; }
        public required bool GoalAchieved { get; init; }
        public required string[] PassedCriteria { get; init; }
        public required string[] FailedCriteria { get; init; }
        /// <summary>Criterion text → short explanation of why it was not met.</summary>
        public Dictionary<string, string> FailureReasons { get; init; } = [];
        /// <summary>Criterion text → 0-100 score (A1: machine-comparable). Derived from pass/fail if the judge omitted it.</summary>
        public Dictionary<string, int> CriterionScores { get; init; } = [];
    }

    public async Task<GoalEvaluation> EvaluateAsync(
        string documentContent,
        string modelUsed,
        string templateType,
        string selectedGoal,
        string[] selectedCriteria,
        CancellationToken ct = default)
    {
        if (modelUsed.Contains(LLMOrchestrator.HeuristicModelName, StringComparison.Ordinal))
        {
            logger.LogDebug("[Judge] Skipping evaluation — heuristic engine output.");
            return PassAll(selectedCriteria);
        }

        try
        {
            var (sys, usr) = PromptBuilder.BuildDocumentJudge(
                documentContent, templateType, selectedGoal, selectedCriteria);

            var (result, _) = await orchestrator.ExecuteAsync(
                "judge-document",
                async (provider, pCt) =>
                {
                    var raw = await provider.CompleteAsync(sys, usr, pCt);
                    return ParseEvaluation(raw, selectedCriteria);
                },
                () => PassAll(selectedCriteria),
                ct);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Judge] Evaluation failed — treating as full pass to avoid blocking generation.");
            return PassAll(selectedCriteria);
        }
    }

    private static GoalEvaluation PassAll(string[] criteria) => new()
    {
        GoalAchievementPct = 85,
        GoalAchieved = true,
        PassedCriteria = criteria,
        FailedCriteria = []
    };

    private static GoalEvaluation ParseEvaluation(string raw, string[] selectedCriteria)
    {
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            var root = doc.RootElement;

            var passedRaw = ParseStringArray(root, "passedCriteria");
            var failedRaw = ParseStringArray(root, "failedCriteria");
            var llmPct = root.TryGetProperty("goalAchievementPct", out var pctEl)
                         && pctEl.ValueKind == JsonValueKind.Number
                ? Math.Clamp(pctEl.GetInt32(), 0, 100) : -1;

            var reasonsByNorm = ParseReasons(root);
            var scoresByNorm  = ParseScores(root);

            // ── Canonicalize the judge's criterion strings back onto the EXACT original criteria ──
            // The judge frequently rewords a criterion (drops backticks, tweaks punctuation/casing)
            // when echoing it into passedCriteria/failedCriteria. The scorecard matches judge output
            // to the original criteria BY TEXT, so any drift left every criterion marked "failed"
            // while GoalAchievementPct (a raw array count) stayed high — the "100% achieved, all
            // criteria failed" contradiction. Matching on a normalized key removes the drift so the
            // percentage AND the per-criterion verdicts are derived from ONE reconciled source.
            if (selectedCriteria.Length > 0)
            {
                var passedNorm = passedRaw.Select(Normalize).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
                var failedNorm = failedRaw.Select(Normalize).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);

                var passed  = new List<string>();
                var failed  = new List<string>();
                var reasons = new Dictionary<string, string>();
                var scores  = new Dictionary<string, int>();

                foreach (var c in selectedCriteria)
                {
                    var n = Normalize(c);
                    bool isPassed;
                    if (Matches(n, passedNorm))      isPassed = true;
                    else if (Matches(n, failedNorm)) isPassed = false;
                    else                             isPassed = llmPct >= 65; // never clearly cited → defer to overall verdict

                    if (isPassed) passed.Add(c); else failed.Add(c);

                    if (!isPassed && LookupByNorm(reasonsByNorm, n) is { Length: > 0 } reason)
                        reasons[c] = reason;
                    scores[c] = LookupScoreByNorm(scoresByNorm, n) ?? (isPassed ? 100 : 0);
                }

                int pct = passed.Count + failed.Count > 0
                    ? (int)Math.Round(100.0 * passed.Count / (passed.Count + failed.Count))
                    : 0;

                return new GoalEvaluation
                {
                    GoalAchievementPct = pct,
                    GoalAchieved       = pct >= 65,
                    PassedCriteria     = [.. passed],
                    FailedCriteria     = [.. failed],
                    FailureReasons     = reasons,
                    CriterionScores    = scores
                };
            }

            // No caller criteria to anchor to — derive purely from the judge arrays / self-reported pct.
            if (passedRaw.Length == 0 && failedRaw.Length == 0)
                return new GoalEvaluation
                {
                    GoalAchievementPct = llmPct < 0 ? 0 : llmPct,
                    GoalAchieved       = llmPct >= 65,
                    PassedCriteria     = [],
                    FailedCriteria     = []
                };

            int pctFromArrays = (int)Math.Round(100.0 * passedRaw.Length / (passedRaw.Length + failedRaw.Length));
            return new GoalEvaluation
            {
                GoalAchievementPct = pctFromArrays,
                GoalAchieved       = pctFromArrays >= 65,
                PassedCriteria     = passedRaw,
                FailedCriteria     = failedRaw
            };
        }
        catch
        {
            return PassAll(selectedCriteria);
        }
    }

    // ── Normalized criterion matching (bridges judge rewording ↔ original criterion text) ──

    /// <summary>Lowercased alphanumerics only — collapses backtick/punctuation/whitespace/casing drift.</summary>
    private static string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s) if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>Exact normalized hit, else a length-guarded containment match (tolerates truncation/expansion).</summary>
    private static bool Matches(string norm, HashSet<string> set)
    {
        if (norm.Length == 0) return false;
        if (set.Contains(norm)) return true;
        foreach (var s in set)
            if (s.Length >= 8 && norm.Length >= 8 && (s.Contains(norm) || norm.Contains(s))) return true;
        return false;
    }

    private static string? LookupByNorm(Dictionary<string, string> byNorm, string n)
    {
        if (byNorm.TryGetValue(n, out var v)) return v;
        foreach (var kv in byNorm)
            if (kv.Key.Length >= 8 && n.Length >= 8 && (kv.Key.Contains(n) || n.Contains(kv.Key))) return kv.Value;
        return null;
    }

    private static int? LookupScoreByNorm(Dictionary<string, int> byNorm, string n)
    {
        if (byNorm.TryGetValue(n, out var v)) return v;
        foreach (var kv in byNorm)
            if (kv.Key.Length >= 8 && n.Length >= 8 && (kv.Key.Contains(n) || n.Contains(kv.Key))) return kv.Value;
        return null;
    }

    /// <summary>failureReasons object → dictionary keyed by the NORMALIZED criterion text.</summary>
    private static Dictionary<string, string> ParseReasons(JsonElement root)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("failureReasons", out var el) && el.ValueKind == JsonValueKind.Object)
            foreach (var prop in el.EnumerateObject())
            {
                var reason = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(reason))
                    d[Normalize(prop.Name)] = reason;
            }
        return d;
    }

    /// <summary>criterionScores object → dictionary keyed by the NORMALIZED criterion text.</summary>
    private static Dictionary<string, int> ParseScores(JsonElement root)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root.TryGetProperty("criterionScores", out var el) && el.ValueKind == JsonValueKind.Object)
            foreach (var prop in el.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.Number)
                    d[Normalize(prop.Name)] = Math.Clamp(prop.Value.GetInt32(), 0, 100);
        return d;
    }

    private static string[] ParseStringArray(System.Text.Json.JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var el)) return [];
        return el.EnumerateArray()
                 .Select(x => x.GetString() ?? string.Empty)
                 .Where(s => !string.IsNullOrWhiteSpace(s))
                 .ToArray();
    }

    private static string ExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : raw;
    }
}
