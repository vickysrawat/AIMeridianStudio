using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.LocalEngine;

namespace MeridianStudio.API.Application.Services;

public sealed class PromptService(
    PayloadCache cache,
    LLMOrchestrator orchestrator,
    LocalCompilationEngine engine,
    IConfiguration config,
    ILogger<PromptService> logger) : IPromptService
{
    public async Task<DeveloperPrompt> GeneratePromptAsync(
        GenerateComponentPromptRequest request, CancellationToken ct = default)
    {
        var cacheKey = cache.ComputeKey(
            new { request.ComponentName, request.TargetLLM, request.Context });

        if (cache.TryGet<DeveloperPrompt>(cacheKey, out var hit))
        {
            logger.LogInformation("[Cache] Prompt hit — key: {K}", cacheKey[..8]);
            return hit;
        }

        var (result, modelUsed) = await orchestrator.ExecuteAsync(
            "generate-component-prompt",
            async (provider, pCt) =>
            {
                var (sys, usr) = PromptBuilder.BuildPrompt(request);
                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return LLMResponseParser.ParsePrompt(raw, request);
            },
            () => engine.CompilePrompt(
                request.ComponentName,
                request.TargetLLM,
                request.Context),
            ct);

        var stamped = result with { ModelUsed = modelUsed };

        if (!modelUsed.Contains(LLMOrchestrator.HeuristicModelName, StringComparison.Ordinal))
        {
            var ttl = TimeSpan.FromHours(config.GetValue<double>("Cache:Prompt:TtlHours", 24.0));
            cache.Set(cacheKey, stamped, ttl);
        }

        return stamped;
    }
}
