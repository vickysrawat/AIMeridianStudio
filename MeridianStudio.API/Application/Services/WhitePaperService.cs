using System.Text;
using System.Text.Json;
using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Artifacts;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.Persistence;
using MeridianStudio.API.Infrastructure.Security;
using MeridianStudio.API.Infrastructure.Tokenization;
using MeridianStudio.API.Infrastructure.WebSearch;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Synthesizes a market/competitive white paper for a specific domain·subdomain·scenario, driven by a
/// Research run, a selected opportunity, a use-case assessment, or (legacy) arbitrary artifacts.
/// It reuses the rich research payload (competitor strategic playbooks, pain points, opportunity
/// narrative + scores) and runs fresh, domain-aware live research (competitors/sizing/differentiation)
/// so empirical claims can be cited [S#] (guardrails). Falls back to deterministic assembly offline.
/// Persisted as a versioned "whitepaper" Document artifact.
/// </summary>
public sealed class WhitePaperService(
    IArtifactStore store,
    PayloadCache cache,
    WebResearchEnricher enricher,
    LLMOrchestrator orchestrator,
    ITenantAccessor tenant,
    DocumentValidationService validation,
    ITokenCounter tokens,
    ILogger<WhitePaperService> logger)
{
    private const int PerArtifactCharCap = 4000;
    private const int TotalContextCharCap = 40000;

    private sealed record WpContext(
        string Title,
        string Domain,
        string SubDomain,
        string Scenario,
        IReadOnlyList<CompetitorInsight> Competitors,
        IReadOnlyList<PainPoint> PainPoints,
        IReadOnlyList<PrioritizedItem> Opportunities,
        PrioritizedItem? Focus,
        string? SourceArtifactId);

    public async Task<WhitePaper?> SynthesizeAsync(WhitePaperRequest request, CancellationToken ct = default)
    {
        // Driven modes take precedence; legacy arbitrary-artifact mode is the fallback.
        if (!string.IsNullOrWhiteSpace(request.ResearchArtifactId))
        {
            var ctx = await ResolveResearchAsync(request, ct);
            return ctx is null ? null : await GenerateAsync(ctx, request, ct);
        }
        if (!string.IsNullOrWhiteSpace(request.AssessmentId))
        {
            var ctx = ResolveAssessment(request);
            return ctx is null ? null : await GenerateAsync(ctx, request, ct);
        }
        if (request.ArtifactIds is { Count: > 0 })
            return await SynthesizeFromArtifactsAsync(request, ct);

        logger.LogInformation("[WhitePaper] No driver provided (research/opportunity/assessment/artifacts).");
        return null;
    }

    // ── Resolvers ───────────────────────────────────────────────────────────

    private async Task<WpContext?> ResolveResearchAsync(WhitePaperRequest request, CancellationToken ct)
    {
        var artifact = await store.GetAsync(request.ResearchArtifactId!, tenant.TenantId, ct);
        var research = artifact?.Payload.Deserialize<ResearchResponse>(ArtifactSerialization.Options);
        if (artifact is null || research is null)
        {
            logger.LogInformation("[WhitePaper] Research artifact {Id} not found.", request.ResearchArtifactId);
            return null;
        }

        var domain    = research.Domain;
        var subDomain = artifact.Metadata.SubDomain ?? string.Empty;

        PrioritizedItem? focus = null;
        if (!string.IsNullOrWhiteSpace(request.OpportunityId))
            focus = research.Items.FirstOrDefault(i => i.Id == request.OpportunityId);

        var scenario = focus is not null
            ? $"Opportunity: {focus.Name}\n{focus.Description}"
            : $"A white paper covering the {subDomain} sub-domain within {domain}.";

        var title = request.Title
            ?? (focus is not null
                ? $"{focus.Name} — {domain} White Paper"
                : $"{(string.IsNullOrWhiteSpace(subDomain) ? domain : subDomain)} — Market & Opportunity White Paper");

        return new WpContext(
            title, domain, subDomain, scenario,
            research.CompetitorInsights,
            research.PainPoints,
            focus is not null ? [focus] : research.Items,
            focus,
            artifact.Metadata.ArtifactId);
    }

    private WpContext? ResolveAssessment(WhitePaperRequest request)
    {
        if (!cache.TryGet<Assessment>($"assess-by-id:{request.AssessmentId}", out var a) || a is null)
        {
            logger.LogInformation("[WhitePaper] Assessment {Id} not found in cache.", request.AssessmentId);
            return null;
        }

        var scenarioParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(a.UseCase)) scenarioParts.Add(a.UseCase);
        if (!string.IsNullOrWhiteSpace(a.ExecutiveSummary)) scenarioParts.Add(a.ExecutiveSummary);
        foreach (var s in a.Sections.Take(4)) scenarioParts.Add($"{s.Title}: {s.Body}");

        var title = request.Title ?? $"{a.Title} — White Paper";
        return new WpContext(
            title, a.Domain, string.Empty,
            string.Join("\n\n", scenarioParts),
            Competitors: [],
            PainPoints: [],
            Opportunities: [],
            Focus: null,
            SourceArtifactId: null);
    }

    // ── Driven generation (fresh research → rich material → guarded prompt) ─────

    private async Task<WhitePaper> GenerateAsync(WpContext ctx, WhitePaperRequest request, CancellationToken ct)
    {
        // Guardrail #2 — fresh domain-aware research (competitors/sizing/differentiation), cached; fail-soft.
        ResearchSourceDto[] sources = [];
        var factsBrief = string.Empty;
        string[] sourcesQueried = [];
        if (request.GroundWithFreshResearch && enricher.IsLiveSearchAvailable)
        {
            try
            {
                var vendors = ctx.Competitors.Select(c => c.CompetitorName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                var (live, brief) = await enricher.EnrichDocumentAsync(
                    ctx.Domain, ctx.SubDomain, solutionType: string.Empty,
                    templateType: "white-paper", title: ctx.Title, vendors, ct);
                if (live.HasData)
                    sources = live.Results.Select(r => new ResearchSourceDto(
                        r.Title, r.Url,
                        r.PublishedAt is { } d ? $"{r.Source}, {d:yyyy-MM}" : r.Source,
                        r.Excerpt)).ToArray();
                factsBrief = brief ?? string.Empty;
                sourcesQueried = live.SourcesQueried;
                logger.LogInformation("[WhitePaper] Live grounding: {N} source(s){B}.",
                    sources.Length, string.IsNullOrWhiteSpace(factsBrief) ? "" : " + facts-brief");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[WhitePaper] Live grounding failed — continuing from research payload.");
            }
        }

        var material = BuildMaterialBlock(ctx);

        var (content, modelUsed, attempts) = await orchestrator.ExecuteWithTraceAsync(
            "whitepaper",
            async (provider, pCt) =>
            {
                var (sys, usr) = PromptBuilder.BuildWhitePaper(
                    ctx.Title, ctx.Domain, ctx.SubDomain, ctx.Scenario, material, sources, factsBrief, tokens);
                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return ExtractMarkdown(raw);
            },
            () => AssembleHeuristicDriven(ctx, material, sources),
            ct);

        content = await validation.RepairContentAsync(content, "whitepaper", ct);
        content = Infrastructure.Documents.MarkdownSanitizer.StripHardBreakBackslashes(content);

        var provenance = OutputProvenance.From(
            modelUsed, attempts,
            liveSources: [.. sources.Select(s => s.Title)],
            factChecked: false);

        return await PersistAsync(ctx.Title, content, modelUsed,
            ctx.SourceArtifactId is null ? [] : [ctx.SourceArtifactId],
            provenance, sourcesQueried, ct);
    }

    private static string BuildMaterialBlock(WpContext ctx) =>
        GroundingMaterialBuilder.BuildOpportunityMaterial(
            ctx.Competitors, ctx.PainPoints, ctx.Focus, ctx.Opportunities);

    private static string AssembleHeuristicDriven(WpContext ctx, string material, ResearchSourceDto[] sources)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {ctx.Title}").AppendLine();
        sb.AppendLine("## Executive Summary").AppendLine();
        sb.AppendLine($"This white paper examines the **{(string.IsNullOrWhiteSpace(ctx.SubDomain) ? ctx.Domain : ctx.SubDomain)}** space within {ctx.Domain}. " +
                      "It was assembled offline (no model available); the sections below reflect the gathered material.").AppendLine();
        sb.AppendLine("## The Opportunity / Scenario").AppendLine().AppendLine(ctx.Scenario).AppendLine();
        sb.AppendLine("## Material").AppendLine().AppendLine(material);
        if (sources.Length > 0)
        {
            sb.AppendLine("## Sources").AppendLine();
            for (var i = 0; i < sources.Length; i++)
                sb.AppendLine($"- [S{i + 1}] {sources[i].Title}{(string.IsNullOrWhiteSpace(sources[i].Url) ? "" : $" — {sources[i].Url}")}");
        }
        return sb.ToString();
    }

    // ── Persistence (shared) ────────────────────────────────────────────────

    private async Task<WhitePaper> PersistAsync(
        string title, string content, string modelUsed, IReadOnlyList<string> sourceArtifactIds,
        OutputProvenance? provenance, string[] sourcesQueried, CancellationToken ct)
    {
        var paper = new WhitePaper
        {
            Id = ArtifactSerialization.NewArtifactId(),
            Title = title,
            Content = content,
            SourceArtifactIds = sourceArtifactIds,
            ModelUsed = modelUsed,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Provenance = provenance,
            SourcesQueried = sourcesQueried
        };

        try
        {
            var meta = new ArtifactMetadata
            {
                ArtifactId = paper.Id,
                Kind = ArtifactKind.Document,
                Title = paper.Title,
                ModelUsed = modelUsed,
                RequestHash = string.Join(",", sourceArtifactIds.OrderBy(x => x)),
                LineageId = $"whitepaper:{paper.Title.Trim().ToLowerInvariant()}",
                TenantId = tenant.TenantId,
                CreatedBy = tenant.UserId,
                Tags = ["whitepaper", .. sourceArtifactIds.Select(id => $"source:{id}")]
            };
            var saved = await store.SaveAsync(paper, meta, ct);
            return paper with { Id = saved.Metadata.ArtifactId };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[WhitePaper] Persist failed — returning unsaved paper.");
            return paper;
        }
    }

    // ── Legacy: arbitrary artifact concatenation ───────────────────────────────

    private async Task<WhitePaper?> SynthesizeFromArtifactsAsync(WhitePaperRequest request, CancellationToken ct)
    {
        var artifacts = await store.GetManyAsync(request.ArtifactIds!, tenant.TenantId, ct);
        if (artifacts.Count == 0)
        {
            logger.LogInformation("[WhitePaper] No resolvable artifacts for tenant {Tenant}.", tenant.TenantId);
            return null;
        }

        var title = request.Title ?? "White Paper";
        var context = BuildContext(artifacts);
        var sourceIds = artifacts.Select(a => a.Metadata.ArtifactId).ToList();

        var (content, modelUsed, attempts) = await orchestrator.ExecuteWithTraceAsync(
            "whitepaper",
            async (provider, pCt) =>
            {
                var (sys, usr) = BuildLegacyPrompt(title, context);
                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return ExtractMarkdown(raw);
            },
            () => AssembleHeuristic(title, artifacts),
            ct);

        content = await validation.RepairContentAsync(content, "whitepaper", ct);
        content = Infrastructure.Documents.MarkdownSanitizer.StripHardBreakBackslashes(content);

        var provenance = OutputProvenance.From(modelUsed, attempts, factChecked: false);
        return await PersistAsync(title, content, modelUsed, sourceIds, provenance, [], ct);
    }

    private static (string System, string User) BuildLegacyPrompt(string title, string context)
    {
        var system =
            """
            You are a principal analyst writing an executive white paper for a technical and business audience.
            Rules:
            - Ground EVERY factual claim ONLY in the SOURCE MATERIAL provided. Do not invent facts, vendors, or figures.
            - When the material does not support a needed statement, write "[REQUIRED: <what is missing>]" instead of guessing.
            - Cite the source artifact inline as [S#] using the numbers given in the material.
            - GitHub-flavored Markdown: # Title, ## Executive Summary, ## Problem & Market Context, ## Solution Architecture,
              ## Comparative Analysis, ## Recommendations, ## Sources.
            - Respond with ONLY valid JSON of the exact shape {"content":"<the full white paper markdown, using \\n for newlines>"}.
            """;
        var user = $"WHITE PAPER TITLE: {title}\n\nSOURCE MATERIAL (each block is one artifact, numbered [S#]):\n{context}\n\nWrite the complete white paper now.";
        return (system, user);
    }

    private string BuildContext(IReadOnlyList<StoredArtifact> artifacts)
    {
        var sb = new StringBuilder();
        var total = 0;
        for (var i = 0; i < artifacts.Count; i++)
        {
            var block = RenderArtifact(i + 1, artifacts[i]);
            if (block.Length > PerArtifactCharCap) block = block[..PerArtifactCharCap] + "\n…(truncated)…";
            if (total + block.Length > TotalContextCharCap) break;
            sb.AppendLine(block).AppendLine();
            total += block.Length;
        }
        return sb.ToString();
    }

    private static string RenderArtifact(int index, StoredArtifact a)
    {
        var header = $"[S{index}] {a.Metadata.Kind} — \"{a.Metadata.Title}\" (model: {a.Metadata.ModelUsed}, v{a.Metadata.Version})";
        var body = a.Metadata.Kind switch
        {
            ArtifactKind.Research => RenderResearch(a),
            ArtifactKind.Blueprint => RenderBlueprint(a),
            ArtifactKind.Document => TryGet<CorporateDocument>(a)?.Content ?? Raw(a),
            _ => Raw(a)
        };
        return $"{header}\n{body}";
    }

    private static string RenderResearch(StoredArtifact a)
    {
        var r = TryGet<ResearchResponse>(a);
        if (r is null) return Raw(a);
        var sb = new StringBuilder();
        sb.AppendLine($"Domain: {r.Domain}");
        if (r.PainPoints.Count > 0)
            sb.AppendLine("Pain points: " + string.Join("; ", r.PainPoints.Select(p => $"{p.Title} (sev {p.Severity})")));
        if (r.CompetitorInsights.Count > 0)
            sb.AppendLine("Competitors: " + string.Join("; ", r.CompetitorInsights.Select(c => $"{c.CompetitorName} — {c.FeatureGap} [{c.StrategicPlaybook}]")));
        if (r.Items.Count > 0)
            sb.AppendLine("Opportunities: " + string.Join("; ", r.Items.Select(i => $"{i.Name} (value {i.Value})")));
        return sb.ToString();
    }

    private static string RenderBlueprint(StoredArtifact a)
    {
        var b = TryGet<SystemBlueprint>(a);
        if (b is null) return Raw(a);
        var sb = new StringBuilder();
        sb.AppendLine($"Solution: {b.SolutionName} ({b.Domain}{(string.IsNullOrWhiteSpace(b.SolutionType) ? "" : $", {b.SolutionType}")})");
        sb.AppendLine("Core scenario: " + Trim(b.CoreScenario, 800));
        if (b.ArchDecisions.Count > 0)
            sb.AppendLine("Architecture decisions: " + string.Join("; ", b.ArchDecisions.Select(d => $"{d.Decision} → {d.ChosenApproach}")));
        return sb.ToString();
    }

    private static T? TryGet<T>(StoredArtifact a) where T : class
    {
        try { return a.Payload.Deserialize<T>(ArtifactSerialization.Options); }
        catch { return null; }
    }

    private static string Raw(StoredArtifact a) => Trim(a.Payload.GetRawText(), PerArtifactCharCap);
    private static string Trim(string s, int cap) => s.Length > cap ? s[..cap] + "…" : s;

    private static string AssembleHeuristic(string title, IReadOnlyList<StoredArtifact> artifacts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {title}").AppendLine().AppendLine("## Executive Summary").AppendLine();
        sb.AppendLine($"This white paper consolidates {artifacts.Count} source artifact(s), assembled offline.").AppendLine();
        for (var i = 0; i < artifacts.Count; i++)
            sb.AppendLine($"## [S{i + 1}] {artifacts[i].Metadata.Title}").AppendLine().AppendLine(RenderArtifact(i + 1, artifacts[i])).AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Extracts the markdown body from a provider response (JSON {"content":"..."}, bare JSON string, or raw).
    /// </summary>
    private static string ExtractMarkdown(string raw)
    {
        var t = raw.Trim();
        if (t.StartsWith("```"))
        {
            var firstNl = t.IndexOf('\n');
            if (firstNl > 0) t = t[(firstNl + 1)..];
            if (t.EndsWith("```")) t = t[..^3].Trim();
        }
        if (t.StartsWith('{') || t.StartsWith('"'))
        {
            try
            {
                using var doc = JsonDocument.Parse(t);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("content", out var c)
                    && c.ValueKind == JsonValueKind.String)
                    return c.GetString()!.Trim();
                if (doc.RootElement.ValueKind == JsonValueKind.String)
                    return doc.RootElement.GetString()!.Trim();
            }
            catch (JsonException) { /* not JSON — fall through */ }
        }
        return t;
    }
}
