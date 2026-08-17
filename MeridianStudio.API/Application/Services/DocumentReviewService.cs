using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.LLM;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Advise-only post-document critic: reviews a finished document against domain / opportunity-fidelity /
/// faithfulness (axes the in-loop <see cref="DocumentGoalJudgeService"/> never checks). Anchors on the
/// grounding blueprint (or assessment brief) when resolvable. Mirrors the other critics: LLM cascade →
/// structured JSON → empty-findings fallback → cached 1 hour. Never gates generation.
/// </summary>
public sealed class DocumentReviewService(
    PayloadCache cache,
    LLMOrchestrator orchestrator,
    ILogger<DocumentReviewService> logger) : IDocumentReviewService
{
    public async Task<DocumentReview> ReviewAsync(DocumentReviewRequest request, CancellationToken ct = default)
    {
        var cacheKey = cache.ComputeKey(new
        {
            op = "document-review",
            request.Content, request.Domain, request.SubDomain, request.TemplateType,
            request.BlueprintId, request.AssessmentId
        });

        if (cache.TryGet<DocumentReview>(cacheKey, out var hit))
        {
            logger.LogDebug("[DocumentReview] Cache hit — key: {K}", cacheKey[..8]);
            return hit;
        }

        var anchor = ResolveAnchor(request);

        var (result, modelUsed) = await orchestrator.ExecuteAsync(
            "review-document",
            async (provider, pCt) =>
            {
                var (sys, usr) = PromptBuilder.BuildDocumentReview(request, anchor);
                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return LLMResponseParser.ParseDocumentReview(raw);
            },
            LLMResponseParser.FallbackDocumentReview,
            ct);

        var stamped = result with { ModelUsed = modelUsed };
        cache.Set(cacheKey, stamped, TimeSpan.FromHours(1));
        return stamped;
    }

    /// <summary>Grounding anchor for the review: the blueprint's scenario, else the assessment brief. Fail-soft.</summary>
    private string? ResolveAnchor(DocumentReviewRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.BlueprintId)
            && cache.TryGet<SystemBlueprint>($"bp-by-id:{req.BlueprintId}", out var bp) && bp is not null)
        {
            return $"Domain: {bp.Domain}\nSub-domain: {bp.SubDomain}\nSolution: {bp.SolutionName}\n" +
                   $"Core scenario: {Trim(bp.CoreScenario, 1200)}";
        }

        if (!string.IsNullOrWhiteSpace(req.AssessmentId)
            && cache.TryGet<Assessment>($"assess-by-id:{req.AssessmentId}", out var a) && a is not null)
        {
            return $"Use case: {a.UseCase}\nExpected outcome: {a.ExpectedOutcome}\n" +
                   $"Executive summary: {Trim(a.ExecutiveSummary, 1200)}";
        }

        return null;
    }

    private static string Trim(string? s, int cap) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Length > cap ? s[..cap] + "…" : s;
}
