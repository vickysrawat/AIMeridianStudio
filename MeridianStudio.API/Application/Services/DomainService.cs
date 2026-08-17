using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.LocalEngine;

namespace MeridianStudio.API.Application.Services;

public sealed class DomainService(
    PayloadCache cache,
    LLMOrchestrator orchestrator,
    LocalCompilationEngine engine,
    IConfiguration config,
    ILogger<DomainService> logger) : IDomainService
{
    // Static key — domain discovery is global, not per-user
    private static readonly object CacheKeyPayload = new { op = "discover-domains", v = 2 };

    public async Task<DomainSuggestions> DiscoverDomainsAsync(CancellationToken ct = default)
    {
        var cacheKey = cache.ComputeKey(CacheKeyPayload);
        var ttl      = TimeSpan.FromHours(config.GetValue<double>("Cache:Domains:TtlHours", 24.0));

        if (cache.TryGet<DomainSuggestions>(cacheKey, out var hit))
        {
            logger.LogInformation("[Cache] DomainSuggestions hit");
            return hit;
        }

        var (result, modelUsed) = await orchestrator.ExecuteAsync(
            "discover-domains",
            async (provider, pCt) =>
            {
                var (sys, usr) = PromptBuilder.BuildDomains();
                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return LLMResponseParser.ParseDomains(raw);
            },
            () => engine.CompileDomains(),
            ct);

        var stamped = result with { ModelUsed = modelUsed };
        cache.Set(cacheKey, stamped, ttl);
        return stamped;
    }
}
