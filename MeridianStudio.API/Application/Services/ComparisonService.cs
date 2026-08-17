using System.Text.Json;
using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Artifacts;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Persistence;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Builds a structured comparison matrix across N artifacts of the same kind. Per-kind extractors
/// project each artifact into a dimension→value map; cells whose value differs from the row's modal
/// value are flagged divergent. Missing fields tolerate null so mixed schema/model versions compare
/// cleanly (edge case #5) — every column carries its provenance.
/// </summary>
public sealed class ComparisonService(IArtifactStore store, ILogger<ComparisonService> logger)
{
    public async Task<ComparisonMatrix?> CompareAsync(
        IReadOnlyList<string> artifactIds, string tenantId, CancellationToken ct = default)
    {
        var artifacts = await store.GetManyAsync(artifactIds, tenantId, ct);
        if (artifacts.Count < 2)
        {
            logger.LogInformation("[Compare] Fewer than 2 resolvable artifacts — nothing to compare.");
            return null;
        }

        var kind = artifacts[0].Metadata.Kind;
        if (artifacts.Any(a => a.Metadata.Kind != kind))
            throw new InvalidOperationException("All artifacts in a comparison must be the same kind.");

        var columns = artifacts.Select(a => new ComparisonColumn(
            a.Metadata.ArtifactId, a.Metadata.Kind, a.Metadata.Title,
            a.Metadata.ModelUsed, a.Metadata.Version, a.Metadata.CreatedAt)).ToList();

        // dimension -> (artifactId -> value)
        var extracted = artifacts.ToDictionary(
            a => a.Metadata.ArtifactId,
            a => ExtractDimensions(a));

        // Union of dimensions, preserving first-seen order.
        var dimensions = new List<string>();
        foreach (var a in artifacts)
            foreach (var dim in extracted[a.Metadata.ArtifactId].Keys)
                if (!dimensions.Contains(dim)) dimensions.Add(dim);

        var rows = new List<ComparisonRow>(dimensions.Count);
        foreach (var dim in dimensions)
        {
            var values = columns.Select(c =>
                extracted[c.ArtifactId].GetValueOrDefault(dim)).ToList();

            // Modal (most common non-null) value; a cell diverges if it differs from it.
            var modal = values.Where(v => v is not null)
                              .GroupBy(v => v)
                              .OrderByDescending(g => g.Count())
                              .Select(g => g.Key)
                              .FirstOrDefault();
            var distinct = values.Where(v => v is not null).Distinct().Count();

            var cells = columns.Select((c, i) =>
                new ComparisonCell(c.ArtifactId, values[i],
                    Divergent: distinct > 1 && values[i] != modal)).ToList();

            rows.Add(new ComparisonRow(dim, cells));
        }

        return new ComparisonMatrix(kind, columns, rows);
    }

    private Dictionary<string, string?> ExtractDimensions(StoredArtifact a)
    {
        try
        {
            return a.Metadata.Kind switch
            {
                ArtifactKind.Research => ResearchDims(Deserialize<ResearchResponse>(a)),
                ArtifactKind.Blueprint => BlueprintDims(Deserialize<SystemBlueprint>(a)),
                ArtifactKind.Document => DocumentDims(Deserialize<CorporateDocument>(a)),
                ArtifactKind.TaskSpec => TaskDims(Deserialize<TaskSpec>(a)),
                ArtifactKind.DeveloperPrompt => PromptDims(Deserialize<DeveloperPrompt>(a)),
                _ => []
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Compare] Failed to extract dimensions for {Id}.", a.Metadata.ArtifactId);
            return [];
        }
    }

    private static T Deserialize<T>(StoredArtifact a) where T : class
        => a.Payload.Deserialize<T>(ArtifactSerialization.Options)
           ?? throw new InvalidOperationException($"Payload did not deserialize to {typeof(T).Name}.");

    private static Dictionary<string, string?> ResearchDims(ResearchResponse r) => new()
    {
        ["Domain"] = r.Domain,
        ["Sub-domains"] = string.Join(", ", r.DomainsList),
        ["Pain points"] = r.PainPoints.Count.ToString(),
        ["Top pain point"] = r.PainPoints.OrderByDescending(p => p.Severity).FirstOrDefault()?.Title,
        ["Competitors"] = string.Join(", ", r.CompetitorInsights.Select(c => c.CompetitorName)),
        ["Opportunities"] = r.Items.Count.ToString(),
        ["Top opportunity"] = r.Items.OrderByDescending(i => i.Value).FirstOrDefault()?.Name,
        ["Avg value"] = r.Items.Count > 0 ? r.Items.Average(i => i.Value).ToString("0.0") : null,
        ["Avg urgency"] = r.Items.Count > 0 ? r.Items.Average(i => i.Urgency).ToString("0.0") : null,
        ["Model"] = r.ModelUsed
    };

    private static Dictionary<string, string?> BlueprintDims(SystemBlueprint b) => new()
    {
        ["Solution"] = b.SolutionName,
        ["Domain"] = b.Domain,
        ["Solution type"] = string.IsNullOrWhiteSpace(b.SolutionType) ? null : b.SolutionType,
        ["Arch decisions"] = b.ArchDecisions.Count.ToString(),
        ["Quality attributes"] = b.QualityAttributes.Count.ToString(),
        ["Tech radar layers"] = string.Join(", ", b.TechRadar.Select(t => t.Layer)),
        ["Buy vs build items"] = b.BuyVsBuild.Count.ToString(),
        ["Model"] = b.ModelUsed
    };

    private static Dictionary<string, string?> DocumentDims(CorporateDocument d) => new()
    {
        ["Title"] = d.Title,
        ["Template"] = d.TemplateType,
        ["Goal achievement"] = $"{d.GoalAchievementPct}%",
        ["Fact-checked"] = d.FactChecked ? "yes" : "no",
        ["Iterations"] = d.IterationsUsed.ToString(),
        ["Word count"] = (d.Content?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length ?? 0).ToString(),
        ["Model"] = d.ModelUsed
    };

    private static Dictionary<string, string?> TaskDims(TaskSpec t) => new()
    {
        ["Task"] = t.TaskName,
        ["Status"] = t.Status,
        ["Progress"] = $"{t.ProgressScore}/100",
        ["Effort"] = t.EstimatedEffort,
        ["Model"] = t.ModelUsed
    };

    private static Dictionary<string, string?> PromptDims(DeveloperPrompt p) => new()
    {
        ["Component"] = p.ComponentName,
        ["Target LLM"] = p.TargetLLM,
        ["Model"] = p.ModelUsed
    };
}
