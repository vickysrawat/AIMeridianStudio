using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.LLM;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Critiques a research opportunity BEFORE a blueprint is generated (readiness score, per-field status,
/// clarifying questions, one-click suggestions). Advisory only. Mirrors <see cref="UseCaseAnalysisService"/>:
/// re-fetches the full opportunity material server-side (so it critiques what the blueprint will actually
/// see — the G1 fidelity fix), runs the LLM cascade → structured JSON → heuristic fallback, cached 1 hour.
/// </summary>
public sealed class BlueprintReadinessService(
    PayloadCache cache,
    LLMOrchestrator orchestrator,
    OpportunityGroundingResolver grounding,
    ILogger<BlueprintReadinessService> logger) : IBlueprintReadinessService
{
    public async Task<UseCaseReadiness> AnalyzeAsync(GenerateBlueprintRequest request, CancellationToken ct = default)
    {
        var cacheKey = cache.ComputeKey(new
        {
            op = "blueprint-readiness",
            request.SolutionId, request.SolutionName, request.Domain, request.SubDomain,
            request.SolutionDescription, request.IntegrationSteps,
            request.ResearchArtifactId, request.OpportunityId
        });

        if (cache.TryGet<UseCaseReadiness>(cacheKey, out var hit))
        {
            logger.LogDebug("[BlueprintReadiness] Cache hit — key: {K}", cacheKey[..8]);
            return hit;
        }

        // Critique the ACTUAL material the blueprint will be grounded in (re-fetched server-side).
        var material = await grounding.ResolveMaterialAsync(request.ResearchArtifactId, request.OpportunityId, ct);

        var (result, modelUsed) = await orchestrator.ExecuteAsync(
            "analyze-opportunity",
            async (provider, pCt) =>
            {
                var (sys, usr) = PromptBuilder.BuildOpportunityReadiness(request, material);
                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return LLMResponseParser.ParseOpportunityReadiness(raw, request);
            },
            () => LLMResponseParser.FallbackBlueprintReadiness(request),
            ct);

        var stamped = result with { ModelUsed = modelUsed };
        cache.Set(cacheKey, stamped, TimeSpan.FromHours(1));
        return stamped;
    }
}
