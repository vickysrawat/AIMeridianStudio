using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.LLM;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Critiques a use-case brief BEFORE the assessment is produced and returns a readiness review
/// (score, per-field status, clarifying questions, one-click-applicable suggestions). Advisory only.
/// Non-streaming — mirrors <see cref="MissionSuggestionService"/>: LLM cascade → structured JSON →
/// heuristic fallback → cached per brief for 1 hour.
/// </summary>
public sealed class UseCaseAnalysisService(
    PayloadCache cache,
    LLMOrchestrator orchestrator,
    ILogger<UseCaseAnalysisService> logger) : IUseCaseAnalysisService
{
    public async Task<UseCaseReadiness> AnalyzeAsync(AssessmentRequest request, CancellationToken ct = default)
    {
        var cacheKey = cache.ComputeKey(new
        {
            op = "usecase-readiness",
            request.UseCaseScenario, request.UseCase, request.Context, request.ProblemStatement,
            request.Objective, request.ScopeOfWork, request.ExpectedOutcome, request.Domain
        });

        if (cache.TryGet<UseCaseReadiness>(cacheKey, out var hit))
        {
            logger.LogDebug("[UseCaseReadiness] Cache hit — key: {K}", cacheKey[..8]);
            return hit;
        }

        var (result, modelUsed) = await orchestrator.ExecuteAsync(
            "analyze-usecase",
            async (provider, pCt) =>
            {
                var (sys, usr) = PromptBuilder.BuildUseCaseReadiness(request);
                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return LLMResponseParser.ParseUseCaseReadiness(raw, request);
            },
            () => LLMResponseParser.FallbackUseCaseReadiness(request),
            ct);

        var stamped = result with { ModelUsed = modelUsed };
        cache.Set(cacheKey, stamped, TimeSpan.FromHours(1));
        return stamped;
    }
}
