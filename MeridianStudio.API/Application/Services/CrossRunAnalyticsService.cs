using System.Text.Json;
using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Artifacts;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Persistence;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Aggregates recurring signals across stored research runs — recurring pain points and competitor
/// patterns. Groups by a normalized title/name (semantic clustering is a future enhancement).
///
/// Scale note (edge case #6): on the disk store this loads research payloads into memory and
/// aggregates there — fine for the single-node analysis-hub scale. The EF store will push this into
/// a projection table when it lands. Analyzes the latest version per lineage to avoid rerun double-counting.
/// </summary>
public sealed class CrossRunAnalyticsService(IArtifactStore store, ILogger<CrossRunAnalyticsService> logger)
{
    public async Task<PainPointAnalytics> PainPointsAsync(
        string tenantId, string? domain, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        var research = await LoadResearchAsync(tenantId, domain, from, to, ct);

        var byTitle = new Dictionary<string, List<(PainPoint Pain, string Domain, string ArtifactId)>>(StringComparer.Ordinal);
        foreach (var (r, id) in research)
            foreach (var p in r.PainPoints)
            {
                var key = Normalize(p.Title);
                if (key.Length == 0) continue;
                if (!byTitle.TryGetValue(key, out var list)) byTitle[key] = list = [];
                list.Add((p, r.Domain, id));
            }

        var clusters = byTitle.Values
            .Select(list => new PainPointCluster(
                Title: MostCommon(list.Select(x => x.Pain.Title)),
                Occurrences: list.Count,
                AvgSeverity: Math.Round(list.Average(x => x.Pain.Severity), 1),
                Domains: list.Select(x => x.Domain).Distinct().ToList(),
                SourceArtifactIds: list.Select(x => x.ArtifactId).Distinct().ToList()))
            .OrderByDescending(c => c.Occurrences)
            .ThenByDescending(c => c.AvgSeverity)
            .ToList();

        return new PainPointAnalytics(research.Count, clusters);
    }

    public async Task<CompetitorAnalytics> CompetitorsAsync(
        string tenantId, string? domain, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        var research = await LoadResearchAsync(tenantId, domain, from, to, ct);

        var byName = new Dictionary<string, List<(CompetitorInsight Ci, string ArtifactId)>>(StringComparer.Ordinal);
        foreach (var (r, id) in research)
            foreach (var c in r.CompetitorInsights)
            {
                var key = Normalize(c.CompetitorName);
                if (key.Length == 0) continue;
                if (!byName.TryGetValue(key, out var list)) byName[key] = list = [];
                list.Add((c, id));
            }

        var patterns = byName.Values
            .Select(list => new CompetitorPattern(
                CompetitorName: MostCommon(list.Select(x => x.Ci.CompetitorName)),
                Occurrences: list.Count,
                FeatureGaps: list.Select(x => x.Ci.FeatureGap).Distinct().Take(5).ToList(),
                SourceArtifactIds: list.Select(x => x.ArtifactId).Distinct().ToList()))
            .OrderByDescending(p => p.Occurrences)
            .ToList();

        return new CompetitorAnalytics(research.Count, patterns);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<List<(ResearchResponse Research, string ArtifactId)>> LoadResearchAsync(
        string tenantId, string? domain, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var metas = await store.QueryAsync(new ArtifactQuery
        {
            Kind = ArtifactKind.Research,
            Domain = domain,
            CreatedAfter = from,
            CreatedBefore = to,
            LatestVersionOnly = true,
            Take = 500
        }, tenantId, ct);

        var stored = await store.GetManyAsync(metas.Select(m => m.ArtifactId), tenantId, ct);

        var result = new List<(ResearchResponse, string)>(stored.Count);
        foreach (var s in stored)
        {
            try
            {
                var r = s.Payload.Deserialize<ResearchResponse>(ArtifactSerialization.Options);
                if (r is not null) result.Add((r, s.Metadata.ArtifactId));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Analytics] Skipping unreadable research artifact {Id}.", s.Metadata.ArtifactId);
            }
        }
        return result;
    }

    private static string Normalize(string s)
        => string.Join(' ', (s ?? "").ToLowerInvariant()
            .Trim()
            .Trim('.', '!', '?', ':', ';', ',')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string MostCommon(IEnumerable<string> values)
        => values.GroupBy(v => v).OrderByDescending(g => g.Count()).Select(g => g.Key).First();
}
