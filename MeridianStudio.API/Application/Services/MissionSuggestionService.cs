using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.ExampleBank;
using MeridianStudio.API.Infrastructure.LLM;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Generates contextual tone, goal, and criteria suggestions for a document type
/// via LLM, grounded in domain + solution type.
/// Suggestions are cached per (templateType + domain + solutionType) for 1 hour.
/// Past user selections (from SelectionBankService) are injected as few-shot context.
/// </summary>
public sealed class MissionSuggestionService(
    PayloadCache cache,
    LLMOrchestrator orchestrator,
    SelectionBankService selectionBank,
    ILogger<MissionSuggestionService> logger) : IMissionSuggestionService
{
    public async Task<MissionSuggestions> GetSuggestionsAsync(
        MissionSuggestionsRequest request, CancellationToken ct = default)
    {
        var persona = PersonaRegistry.Get(request.TemplateType);

        var cacheKey = cache.ComputeKey(
            new { request.TemplateType, request.Domain, request.SolutionType });

        if (cache.TryGet<MissionSuggestions>(cacheKey, out var hit))
        {
            logger.LogDebug("[MissionSuggestions] Cache hit — key: {K}", cacheKey[..8]);
            return hit;
        }

        // Inject past selections as few-shot context so popular choices surface first
        var pastContext = await selectionBank.GetContextAsync(
            request.TemplateType,
            request.Domain ?? string.Empty,
            request.SolutionType ?? string.Empty,
            ct);

        var (result, modelUsed) = await orchestrator.ExecuteAsync(
            "mission-suggestions",
            async (provider, pCt) =>
            {
                var (sys, usr) = PromptBuilder.BuildMissionSuggestions(
                    persona.Persona,
                    persona.SecondaryAudience,
                    request.TemplateType,
                    request.Domain ?? string.Empty,
                    request.SolutionType ?? string.Empty,
                    request.BlueprintContext ?? string.Empty,
                    pastContext);

                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return LLMResponseParser.ParseMissionSuggestions(raw, persona.Persona, persona.SecondaryAudience);
            },
            () => LLMResponseParser.FallbackFor(persona.Persona, persona.SecondaryAudience),
            ct);

        var stamped = result with { ModelUsed = modelUsed };

        var ttl = TimeSpan.FromHours(1);
        cache.Set(cacheKey, stamped, ttl);

        return stamped;
    }
}
