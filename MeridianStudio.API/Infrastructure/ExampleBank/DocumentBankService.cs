using System.Text.Json;
using MeridianStudio.API.Infrastructure.LLM.Embedding;
using MeridianStudio.API.Infrastructure.Retrieval;

namespace MeridianStudio.API.Infrastructure.ExampleBank;

/// <summary>
/// Stores documents where the goal was fully achieved (GoalAchieved == true).
/// These are used as few-shot examples in future BuildDocument prompts.
/// Storage: {bankRoot}/documents/{templateType}.json, max 5 entries.
/// On overflow, the entry with the lowest GoalAchievementPct is evicted.
///
/// Retrieval (Phase 2): hard-filter by Domain to preserve the vertical guarantee, then rank
/// the survivors by semantic similarity to the request's sub-domain + goal + criteria, so the
/// chosen examples are sub-domain specific — not merely the same broad domain. Falls back to
/// the legacy domain-boost + score ordering when embeddings are unavailable.
/// </summary>
public sealed class DocumentBankService(
    IConfiguration config,
    IEmbeddingProvider embedder,
    ILogger<DocumentBankService> logger)
{
    private static readonly JsonSerializerOptions _json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string _root = Path.Combine(
        config.GetValue<string>("ExampleBank:Root", "example-bank")!, "documents");

    private readonly SemaphoreSlim _lock = new(1, 1);
    private const int MaxEntries = 5;
    private const int ExcerptLength = 1000;
    private const int TopK = 2;

    public sealed record DocumentEntry
    {
        public required string Title { get; init; }
        public required string Domain { get; init; }
        public required string SolutionType { get; init; }
        public required string GoalUsed { get; init; }
        public required string[] CriteriaUsed { get; init; }
        public required string Excerpt { get; init; }
        public required int GoalAchievementPct { get; init; }
        public required int IterationsUsed { get; init; }
        public required bool WasRefined { get; init; }
        public required string CreatedAt { get; init; }

        // ── Phase 2 additions (nullable so existing bank files still deserialize) ──
        public string? SubDomain { get; init; }
        public float[]? Embedding { get; init; }
        public string? EmbeddingSpace { get; init; }
    }

    public async Task RecordAsync(
        string templateType,
        string domain,
        string subDomain,
        string solutionType,
        string title,
        string content,
        string goalUsed,
        string[] criteriaUsed,
        int goalAchievementPct,
        int iterationsUsed,
        bool wasRefined,
        CancellationToken ct = default)
    {
        var excerpt = content.Length > ExcerptLength
            ? content[..ExcerptLength] + "..."
            : content;

        // Embed at record time so retrieval is a cheap query-only embedding + cosine.
        float[]? embedding = null;
        string?  space     = null;
        try
        {
            embedding = await embedder.EmbedAsync(
                EmbeddingText(domain, subDomain, solutionType, goalUsed, excerpt), ct);
            space = embedder.SpaceId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[DocumentBank] Embedding failed at record time for {T} — stored without a vector.", templateType);
        }

        var entry = new DocumentEntry
        {
            Title                = title,
            Domain               = domain,
            SubDomain            = subDomain,
            SolutionType         = solutionType,
            GoalUsed             = goalUsed,
            CriteriaUsed         = criteriaUsed,
            Excerpt              = excerpt,
            GoalAchievementPct   = goalAchievementPct,
            IterationsUsed       = iterationsUsed,
            WasRefined           = wasRefined,
            CreatedAt            = DateTimeOffset.UtcNow.ToString("O"),
            Embedding            = embedding,
            EmbeddingSpace       = space
        };

        await _lock.WaitAsync(ct);
        try
        {
            var path = FilePath(templateType);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var entries = await LoadAsync(path);
            entries.Add(entry);

            // Keep only the top MaxEntries by score
            if (entries.Count > MaxEntries)
                entries = [.. entries.OrderByDescending(e => e.GoalAchievementPct).Take(MaxEntries)];

            await File.WriteAllTextAsync(path,
                JsonSerializer.Serialize(entries, _json), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[DocumentBank] Failed to record document for {T}", templateType);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns a formatted few-shot context string for injection into BuildDocument prompts.
    /// Hard-filters by domain, then ranks the survivors by semantic similarity to
    /// (sub-domain + goal + criteria); returns the top entries.
    /// </summary>
    public async Task<string> GetExamplesContextAsync(
        string templateType,
        string domain,
        string subDomain,
        string selectedGoal,
        string[] selectedCriteria,
        CancellationToken ct = default)
    {
        try
        {
            var path = FilePath(templateType);
            if (!File.Exists(path)) return string.Empty;

            var entries = await LoadAsync(path);
            if (entries.Count == 0) return string.Empty;

            // Hard domain filter preserves the vertical guarantee; if nothing matches
            // (e.g. a new domain), fall back to all entries rather than returning none.
            var filtered = entries.Where(e => DomainMatches(e.Domain, domain)).ToList();
            if (filtered.Count == 0) filtered = entries;

            var queryText = BuildQueryText(subDomain, selectedGoal, selectedCriteria);

            List<DocumentEntry> top;
            if (string.IsNullOrWhiteSpace(queryText))
            {
                top = LegacyRank(filtered, domain);
            }
            else
            {
                try
                {
                    top = await SemanticRankAsync(filtered, queryText, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "[DocumentBank] Semantic ranking failed for {T} — using legacy ranking.", templateType);
                    top = LegacyRank(filtered, domain);
                }
            }

            return string.Join("\n\n", top.Take(TopK).Select((e, i) =>
            {
                var subLabel = string.IsNullOrWhiteSpace(e.SubDomain) ? string.Empty : $", {e.SubDomain}";
                return
                    $"--- EXAMPLE {i + 1}: \"{e.Title}\" ({e.GoalAchievementPct}/100{subLabel}, {e.IterationsUsed} pass(es))\n" +
                    $"Goal that was set: \"{e.GoalUsed}\"\n" +
                    $"{e.Excerpt}";
            }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[DocumentBank] Failed to load examples for {T}", templateType);
            return string.Empty;
        }
    }

    // ── ranking ─────────────────────────────────────────────────────────────────

    private async Task<List<DocumentEntry>> SemanticRankAsync(
        List<DocumentEntry> candidates, string queryText, CancellationToken ct)
    {
        var queryVec = await embedder.EmbedAsync(queryText, ct);

        // Use each candidate's stored vector when it is from the current space; otherwise
        // re-embed (batched) so old entries and space changes are handled transparently.
        var vectors  = new float[candidates.Count][];
        var needIdx   = new List<int>();
        var needTexts = new List<string>();

        for (var i = 0; i < candidates.Count; i++)
        {
            var e = candidates[i];
            if (e.Embedding is { Length: > 0 } && e.EmbeddingSpace == embedder.SpaceId)
            {
                vectors[i] = e.Embedding;
            }
            else
            {
                needIdx.Add(i);
                needTexts.Add(EmbeddingText(e.Domain, e.SubDomain, e.SolutionType, e.GoalUsed, e.Excerpt));
            }
        }

        if (needTexts.Count > 0)
        {
            var embedded = await embedder.EmbedBatchAsync(needTexts, ct);
            for (var k = 0; k < needIdx.Count; k++)
                vectors[needIdx[k]] = embedded[k];
        }

        return [.. candidates
            .Select((e, i) => (Entry: e, Score: VectorMath.Cosine(queryVec, vectors[i])))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.GoalAchievementPct)
            .Select(x => x.Entry)];
    }

    private static List<DocumentEntry> LegacyRank(List<DocumentEntry> candidates, string domain) =>
        [.. candidates.OrderByDescending(e =>
            (e.Domain.Contains(domain, StringComparison.OrdinalIgnoreCase) ? 10 : 0) +
            e.GoalAchievementPct)];

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static bool DomainMatches(string entryDomain, string domain) =>
        string.IsNullOrWhiteSpace(domain)
        || string.IsNullOrWhiteSpace(entryDomain)
        || entryDomain.Contains(domain, StringComparison.OrdinalIgnoreCase)
        || domain.Contains(entryDomain, StringComparison.OrdinalIgnoreCase);

    private static string BuildQueryText(string subDomain, string selectedGoal, string[] selectedCriteria)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(subDomain))    parts.Add(subDomain);
        if (!string.IsNullOrWhiteSpace(selectedGoal)) parts.Add(selectedGoal);
        if (selectedCriteria is { Length: > 0 })      parts.Add(string.Join(" ", selectedCriteria));
        return string.Join(" ", parts);
    }

    private static string EmbeddingText(
        string domain, string? subDomain, string solutionType, string goalUsed, string excerpt)
    {
        var head = $"{domain} {subDomain} {solutionType} {goalUsed}".Trim();
        var body = excerpt.Length > 600 ? excerpt[..600] : excerpt;
        return $"{head}\n{body}";
    }

    private string FilePath(string templateType) =>
        Path.Combine(_root, $"{templateType.ToLowerInvariant().Replace(" ", "-")}.json");

    private static async Task<List<DocumentEntry>> LoadAsync(string path)
    {
        if (!File.Exists(path)) return [];
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<DocumentEntry>>(json, _json) ?? [];
    }
}
