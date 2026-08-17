using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.LocalEngine;

namespace MeridianStudio.API.Application.Services;

public sealed class TaskExecutionService(
    PayloadCache cache,
    LLMOrchestrator orchestrator,
    LocalCompilationEngine engine,
    IConfiguration config,
    ILogger<TaskExecutionService> logger) : ITaskExecutionService
{
    public async Task<TaskSpec> ExecuteTaskAsync(
        ExecuteTaskRequest request, CancellationToken ct = default)
    {
        // Ground execution in the design when supplied (G3-A): the blueprint directly, or a synthesised
        // grounding blueprint from the use-case assessment. The fingerprint enters the cache key so
        // grounded/ungrounded runs don't collide and a re-exec after a revision regenerates.
        SystemBlueprint? grounding = null;
        if (!string.IsNullOrWhiteSpace(request.BlueprintId)
            && cache.TryGet<SystemBlueprint>($"bp-by-id:{request.BlueprintId}", out var bp))
            grounding = bp;
        else if (!string.IsNullOrWhiteSpace(request.AssessmentId)
            && cache.TryGet<Assessment>($"assess-by-id:{request.AssessmentId}", out var a) && a is not null)
            grounding = AssessmentGrounding.Synthesise(a);

        var cacheKey = cache.ComputeKey(new
        {
            request.TaskName, request.Context, request.Language, request.BlueprintId, request.AssessmentId,
            GroundingFp = grounding is null ? null : BlueprintFingerprint.Compute(grounding)
        });

        if (cache.TryGet<TaskSpec>(cacheKey, out var hit))
        {
            logger.LogInformation("[Cache] TaskSpec hit — key: {K}", cacheKey[..8]);
            return hit;
        }

        var (result, modelUsed) = await orchestrator.ExecuteAsync(
            "execute-task",
            async (provider, pCt) =>
            {
                var (sys, usr) = PromptBuilder.BuildTask(request, grounding);
                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return LLMResponseParser.ParseTask(raw, request);
            },
            () => engine.CompileTask(
                request.TaskName,
                request.SystemicValue,
                request.EstimatedEffort,
                request.Context,
                request.Language),
            ct);

        var stamped = result with { ModelUsed = modelUsed };

        if (!modelUsed.Contains(LLMOrchestrator.HeuristicModelName, StringComparison.Ordinal))
        {
            var ttl = TimeSpan.FromHours(config.GetValue<double>("Cache:Task:TtlHours", 24.0));
            cache.Set(cacheKey, stamped, ttl);
        }

        return stamped;
    }
}
