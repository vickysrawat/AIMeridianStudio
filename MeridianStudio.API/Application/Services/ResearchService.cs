using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.LocalEngine;
using MeridianStudio.API.Infrastructure.WebSearch;

namespace MeridianStudio.API.Application.Services;

public sealed class ResearchService(
    PayloadCache cache,
    SemanticCache semantic,
    LLMOrchestrator orchestrator,
    LocalCompilationEngine engine,
    WebResearchEnricher enricher,
    IConfiguration config,
    ILogger<ResearchService> logger) : IResearchService
{
    public async Task<ResearchResponse> ResearchAsync(
        ResearchRequest request, CancellationToken ct = default)
    {
        // loadMore bypasses cache
        if (request.LoadMore)
        {
            var excluded = request.ExistingItemIds is { Count: > 0 }
                ? new HashSet<string>(request.ExistingItemIds, StringComparer.Ordinal)
                : null;

            var liveCtx = await GetLiveContextAsync(request, ct);
            var persona = PromptBuilder.BuildResearchPersona(request.Domain ?? string.Empty, request.Weights);

            var (lmResult, lmModel, lmAttempts) = await orchestrator.ExecuteWithTraceAsync(
                "research.loadmore",
                async (provider, pCt) =>
                {
                    var (sys, usr) = PromptBuilder.BuildResearch(request, liveCtx, persona);
                    var raw = await provider.CompleteAsync(sys, usr, pCt);
                    return LLMResponseParser.ParseResearch(raw, request.Keywords);
                },
                () => engine.CompileResearch(
                    request.Keywords,
                    request.Page > 1 ? request.Page : 2,
                    excluded),
                ct);

            return (lmResult with
            {
                ModelUsed = lmModel,
                LiveSourcesQueried = liveCtx.SourcesQueried,
                Provenance = OutputProvenance.From(lmModel, lmAttempts, liveCtx.SourcesQueried)
            });
        }

        // Cache key includes subdomain + weights so different weight profiles get fresh results
        var cacheKey = cache.ComputeKey(new
        {
            Keywords  = request.SubDomain ?? request.Keywords,
            request.Domain,
            Weights   = request.Weights?.Normalised(),
            request.IsRerun
        });

        if (request.IsRerun)
            cache.Evict(cacheKey);

        if (cache.TryGet<ResearchResponse>(cacheKey, out var hit))
        {
            logger.LogInformation("[Cache] Research hit — key: {K}", cacheKey[..8]);
            return hit;
        }

        // Semantic pre-check (B3, opt-in): map a near-duplicate request to an equivalent prior key.
        var semanticQuery = $"{request.Domain} {request.SubDomain ?? request.Keywords}".Trim();
        if (!request.IsRerun)
        {
            var semKey = await semantic.ResolveKeyAsync(semanticQuery, ct);
            if (semKey is not null && cache.TryGet<ResearchResponse>(semKey, out var semHit))
            {
                logger.LogInformation("[Cache] Research semantic hit — mapped to key {K}", semKey[..8]);
                return semHit;
            }
        }

        // Run live web search in parallel with LLM orchestrator setup
        var liveContext = await GetLiveContextAsync(request, ct);
        var resPersona  = PromptBuilder.BuildResearchPersona(request.Domain ?? string.Empty, request.Weights);

        var (result, modelUsed, attempts) = await orchestrator.ExecuteWithTraceAsync(
            "research",
            async (provider, pCt) =>
            {
                var (sys, usr) = PromptBuilder.BuildResearch(request, liveContext, resPersona);
                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return LLMResponseParser.ParseResearch(raw, request.Keywords);
            },
            () => engine.CompileResearch(request.Keywords, 1, null),
            ct);

        var ttl     = TimeSpan.FromHours(config.GetValue<double>("Cache:Research:TtlHours", 1.0));
        var stamped = result with
        {
            ModelUsed = modelUsed,
            LiveSourcesQueried = liveContext.SourcesQueried,
            Provenance = OutputProvenance.From(modelUsed, attempts, liveContext.SourcesQueried)
        };
        cache.Set(cacheKey, stamped, ttl);
        await semantic.RememberAsync(semanticQuery, cacheKey, ct);
        return stamped;
    }

    private async Task<LiveResearchContext> GetLiveContextAsync(
        ResearchRequest request, CancellationToken ct)
    {
        try
        {
            var subDomain = request.SubDomain ?? request.Keywords;
            var domain    = request.Domain    ?? string.Empty;
            var weights   = request.Weights   ?? new DimensionWeights();
            return await enricher.EnrichAsync(subDomain, domain, weights, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Research] Live enrichment failed — continuing without live data");
            return LiveResearchContext.Empty;
        }
    }
}
