using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Artifacts;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.Security;

namespace MeridianStudio.API.Application.Services.Persistence;

/// <summary>
/// Persistence decorators wrap the concrete application services and save each generated result
/// as a durable artifact — without touching the inner services. Saves are best-effort:
/// a store failure is swallow-and-logged so generation never fails on a storage hiccup.
///
/// Streaming surfaces (blueprint/assessment SSE) are NOT persisted here (a decorator can't cleanly
/// buffer an IAsyncEnumerable and must not persist a partial on client disconnect) — those are
/// persisted at the endpoint on successful stream completion. See plan edge case #4.
/// </summary>
internal static class PersistenceGuard
{
    public static async Task SafeSaveAsync<T>(
        IArtifactStore store, T payload, ArtifactMetadata meta, ILogger logger, CancellationToken ct)
    {
        try
        {
            await store.SaveAsync(payload, meta, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Artifacts] Persist failed for {Kind} lineage {Lineage} — continuing.",
                meta.Kind, meta.LineageId);
        }
    }
}

public sealed class PersistingResearchService(
    ResearchService inner,
    IArtifactStore store,
    PayloadCache cache,
    ITenantAccessor tenant,
    ILogger<PersistingResearchService> logger) : IResearchService
{
    public async Task<ResearchResponse> ResearchAsync(ResearchRequest request, CancellationToken ct = default)
    {
        var result = await inner.ResearchAsync(request, ct);

        // loadMore returns partial additional items, not a full research set — never version it (#2).
        if (!request.LoadMore)
        {
            var hash = cache.ComputeKey(request);
            var meta = ArtifactProjection.ForResearch(result, request, hash, tenant.TenantId, tenant.UserId);
            await PersistenceGuard.SafeSaveAsync(store, result, meta, logger, ct);
        }

        return result;
    }
}

public sealed class PersistingTaskExecutionService(
    TaskExecutionService inner,
    IArtifactStore store,
    PayloadCache cache,
    ITenantAccessor tenant,
    ILogger<PersistingTaskExecutionService> logger) : ITaskExecutionService
{
    public async Task<TaskSpec> ExecuteTaskAsync(ExecuteTaskRequest request, CancellationToken ct = default)
    {
        var result = await inner.ExecuteTaskAsync(request, ct);
        var hash = cache.ComputeKey(request);
        var meta = ArtifactProjection.ForTask(result, request, hash, tenant.TenantId, tenant.UserId);
        await PersistenceGuard.SafeSaveAsync(store, result, meta, logger, ct);
        return result;
    }
}

public sealed class PersistingDocumentService(
    DocumentService inner,
    IArtifactStore store,
    PayloadCache cache,
    ITenantAccessor tenant,
    ILogger<PersistingDocumentService> logger) : IDocumentService
{
    public async Task<CorporateDocument> GenerateDocumentAsync(GenerateDocumentRequest request, CancellationToken ct = default)
    {
        var result = await inner.GenerateDocumentAsync(request, ct);

        // Attach provenance from fields the result already carries (DocumentService stays untouched).
        // ProvidersAttempted is the winning model only — a lightweight trace sufficient for confidence.
        var sources = request.ResearchSources?.Select(s => s.Title).ToArray() ?? [];
        result = result with
        {
            Provenance = result.Provenance ?? OutputProvenance.From(
                result.ModelUsed, [result.ModelUsed], sources, result.FactChecked)
        };

        var hash = cache.ComputeKey(request);
        var meta = ArtifactProjection.ForDocument(result, request, hash, tenant.TenantId, tenant.UserId);
        await PersistenceGuard.SafeSaveAsync(store, result, meta, logger, ct);
        return result;
    }

    // By-id section fixes mutate an existing document in place — not persisted as a new artifact.
    public Task<CorporateDocument> FixSectionAsync(StructuredDocument doc, string criterionId, CancellationToken ct = default)
        => inner.FixSectionAsync(doc, criterionId, ct);
}

public sealed class PersistingAssessmentService(
    AssessmentService inner,
    IArtifactStore store,
    PayloadCache cache,
    ITenantAccessor tenant,
    ILogger<PersistingAssessmentService> logger) : IAssessmentService
{
    // Stream + chat pass through (streaming can't be cleanly buffered by a decorator).
    public IAsyncEnumerable<(string Event, string Data)> StreamAssessmentAsync(
        AssessmentRequest request, CancellationToken ct = default)
        => inner.StreamAssessmentAsync(request, ct);

    public IAsyncEnumerable<(string Event, string Data)> StreamChatAsync(
        string assessmentId, BlueprintChatRequest request, CancellationToken ct = default)
        => inner.StreamChatAsync(assessmentId, request, ct);

    // A patch is now DURABLE — a new artifact version (parity with blueprint revisions), so a use-case
    // document/task can re-resolve the assessment (assess-by-id) after a restart.
    public async Task<Assessment?> PatchAssessmentAsync(
        string assessmentId, PatchAssessmentRequest patch, CancellationToken ct = default)
    {
        var result = await inner.PatchAssessmentAsync(assessmentId, patch, ct);
        if (result is not null)
        {
            var hash = cache.ComputeKey(new
            {
                result.Id, result.ExecutiveSummary, result.Sections,
                result.Recommendations, result.Risks, result.NextSteps
            });
            var meta = ArtifactProjection.ForAssessment(result, hash, tenant.TenantId, tenant.UserId);
            await PersistenceGuard.SafeSaveAsync(store, result, meta, logger, ct);
        }
        return result;
    }
}

public sealed class PersistingPromptService(
    PromptService inner,
    IArtifactStore store,
    PayloadCache cache,
    ITenantAccessor tenant,
    ILogger<PersistingPromptService> logger) : IPromptService
{
    public async Task<DeveloperPrompt> GeneratePromptAsync(GenerateComponentPromptRequest request, CancellationToken ct = default)
    {
        var result = await inner.GeneratePromptAsync(request, ct);
        var hash = cache.ComputeKey(request);
        var meta = ArtifactProjection.ForPrompt(result, request, hash, tenant.TenantId, tenant.UserId);
        await PersistenceGuard.SafeSaveAsync(store, result, meta, logger, ct);
        return result;
    }
}

public sealed class PersistingBlueprintService(
    BlueprintService inner,
    IArtifactStore store,
    PayloadCache cache,
    ITenantAccessor tenant,
    ILogger<PersistingBlueprintService> logger) : IBlueprintService
{
    public async Task<SystemBlueprint> GenerateBlueprintAsync(GenerateBlueprintRequest request, CancellationToken ct = default)
    {
        var result = await inner.GenerateBlueprintAsync(request, ct);
        var hash = cache.ComputeKey(request);
        var meta = ArtifactProjection.ForBlueprint(result, request, hash, tenant.TenantId, tenant.UserId);
        await PersistenceGuard.SafeSaveAsync(store, result, meta, logger, ct);
        return result;
    }

    // Streaming + patch/regenerate flow through unchanged; streaming persistence is handled at the
    // endpoint on the terminal "complete" event (edge case #4).
    public IAsyncEnumerable<(string Event, string Data)> StreamBlueprintAsync(
        GenerateBlueprintRequest request, CancellationToken ct = default)
        => inner.StreamBlueprintAsync(request, ct);

    // A revision is now DURABLE: persist a new artifact version (dedup-keyed on the content fingerprint
    // so an unchanged re-patch is a no-op). Fixes the "patch only lives in the cache, lost on restart" gap.
    public async Task<SystemBlueprint?> PatchBlueprintAsync(string blueprintId, PatchBlueprintRequest patch, CancellationToken ct = default)
    {
        var result = await inner.PatchBlueprintAsync(blueprintId, patch, ct);
        if (result is not null)
        {
            var fingerprint = BlueprintFingerprint.Compute(result);
            var meta = ArtifactProjection.ForBlueprintRevision(result, fingerprint, tenant.TenantId, tenant.UserId);
            await PersistenceGuard.SafeSaveAsync(store, result, meta, logger, ct);
        }
        return result;
    }

    public IAsyncEnumerable<(string Event, string Data)> RegenerateTopologyAsync(
        string blueprintId, CancellationToken ct = default)
        => inner.RegenerateTopologyAsync(blueprintId, ct);
}
