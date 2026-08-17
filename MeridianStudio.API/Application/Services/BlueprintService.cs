using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.LLM.Providers;
using MeridianStudio.API.Infrastructure.LocalEngine;
using MeridianStudio.API.Infrastructure.Realtime;

namespace MeridianStudio.API.Application.Services;

public sealed class BlueprintService(
    PayloadCache cache,
    LLMOrchestrator orchestrator,
    IEnumerable<ILLMProvider> providers,
    LocalCompilationEngine engine,
    SolutionClassifierService classifier,
    DocumentValidationService validation,
    OpportunityGroundingResolver grounding,
    IConfiguration config,
    ILogger<BlueprintService> logger) : IBlueprintService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented               = false
    };

    private readonly IReadOnlyList<ILLMProvider> _providers = [.. providers];

    private void Emit(string type, string provider, string op) =>
        orchestrator.RecordStatus(type, provider, op);

    /// <summary>Cache key includes the grounding ids so re-grounding a solution never serves a stale blueprint.</summary>
    private string BlueprintCacheKey(GenerateBlueprintRequest request) =>
        cache.ComputeKey(new
        {
            request.SolutionId,
            request.SolutionName,
            request.Domain,
            request.SubDomain,
            Desc = request.SolutionDescription is { Length: > 120 } d ? d[..120] : request.SolutionDescription,
            request.ResearchArtifactId,
            request.OpportunityId
        });

    public async Task<SystemBlueprint> GenerateBlueprintAsync(
        GenerateBlueprintRequest request, CancellationToken ct = default)
    {
        var cacheKey = BlueprintCacheKey(request);

        if (cache.TryGet<SystemBlueprint>(cacheKey, out var hit))
        {
            logger.LogInformation("[Cache] Blueprint hit — key: {K}", cacheKey[..8]);
            return hit;
        }

        var material = await grounding.ResolveMaterialAsync(request.ResearchArtifactId, request.OpportunityId, ct);

        var (result, modelUsed) = await orchestrator.ExecuteAsync(
            "generate-blueprint",
            async (provider, pCt) =>
            {
                var (sys, usr) = PromptBuilder.BuildBlueprint(request, material);
                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return LLMResponseParser.ParseBlueprint(raw, request);
            },
            () => engine.CompileBlueprint(
                request.SolutionId,
                request.SolutionName,
                request.Domain,
                request.SubDomain,
                request.SolutionDescription),
            ct);

        // Stamp user-authored pre-generation context so it grounds documents/chat and feeds classification.
        if (!string.IsNullOrWhiteSpace(request.ProjectNotes))
            result = result with { ProjectNotes = request.ProjectNotes };

        // Determine solution type. Priority: caller override → the generation LLM's own classification
        // (server-side, not client-spoofable) canonicalised to the known vocabulary → keyword heuristic.
        var (solutionType, confidence) = ResolveSolutionType(request.OverrideSolutionType, result);

        var stamped = result with
        {
            ModelUsed              = modelUsed,
            SolutionType           = solutionType,
            SolutionTypeConfidence = confidence
        };

        // Browserless Mermaid repair pass (no-op if disabled or no mermaid blocks present).
        stamped = stamped with
        {
            BaseTopology = await validation.RepairContentAsync(stamped.BaseTopology, "generate-blueprint", ct)
        };

        // Always cache — including heuristic fallback — so DocumentService and the
        // chat service can retrieve the blueprint by Id via the bp-by-id: secondary index.
        var ttl = TimeSpan.FromHours(config.GetValue<double>("Cache:Blueprint:TtlHours", 24.0));
        cache.Set(cacheKey, stamped, ttl);
        cache.Set($"bp-by-id:{stamped.Id}", stamped, ttl);

        logger.LogInformation("[Blueprint] SolutionType: {T} ({C:P0})", solutionType, confidence);
        return stamped;
    }

    public async IAsyncEnumerable<(string Event, string Data)> StreamBlueprintAsync(
        GenerateBlueprintRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Evict any stale cached blueprint — user explicitly requested a fresh generation.
        // A fresh LLM result will be cached afterwards so DocumentService can still use
        // the bp-by-id: secondary index when generating documents.
        var cacheKey = BlueprintCacheKey(request);
        cache.Evict(cacheKey);

        // 2. Try each configured provider — get the first chunk before committing
        var material   = await grounding.ResolveMaterialAsync(request.ResearchArtifactId, request.OpportunityId, ct);
        var (sys, usr) = PromptBuilder.BuildBlueprint(request, material);
        var rawBuilder  = new StringBuilder();
        var modelUsed   = LLMOrchestrator.HeuristicModelName;
        var streamOk    = false;

        IAsyncEnumerator<string>? active = null;
        string? firstChunk = null;

        foreach (var provider in _providers)
        {
            if (!provider.IsConfigured) continue;

            Emit("attempting", provider.Name, "generate-blueprint");

            var enumerator = provider.StreamAsync(sys, usr, ct).GetAsyncEnumerator(ct);
            bool hasFirst;

            try { hasFirst = await enumerator.MoveNextAsync(); }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "[Blueprint/Stream] {P} failed before first chunk — trying next.", provider.Name);
                Emit("failed", provider.Name, "generate-blueprint");
                await enumerator.DisposeAsync();
                continue;
            }

            if (!hasFirst)
            {
                Emit("failed", provider.Name, "generate-blueprint");
                await enumerator.DisposeAsync();
                continue;
            }

            active     = enumerator;
            firstChunk = enumerator.Current;
            modelUsed  = provider.Name;
            streamOk   = true;
            Emit("succeeded", provider.Name, "generate-blueprint");
            break;
        }

        // 3. If a provider is ready, stream its output
        if (streamOk && active is not null && firstChunk is not null)
        {
            rawBuilder.Append(firstChunk);
            yield return ("chunk", firstChunk);

            // await using is try/finally — yield is permitted inside it
            await using (active)
            {
                while (await active.MoveNextAsync())
                {
                    rawBuilder.Append(active.Current);
                    yield return ("chunk", active.Current);
                }
            }
        }

        // 4. Parse streamed text or fall back to heuristic engine
        SystemBlueprint result;

        if (streamOk && rawBuilder.Length > 0)
        {
            try
            {
                // Streaming LLMs may wrap JSON in Markdown code fences (```json ... ```)
                // when responseMimeType enforcement is removed. Pre-extract the outermost
                // JSON object so ParseBlueprint always receives a string starting with '{'.
                var rawText = rawBuilder.ToString();
                var jStart  = rawText.IndexOf('{');
                var jEnd    = rawText.LastIndexOf('}');
                var toParse = jStart >= 0 && jEnd > jStart ? rawText[jStart..(jEnd + 1)] : rawText;
                result = LLMResponseParser.ParseBlueprint(toParse, request);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Blueprint/Stream] Parse failed — falling back to engine.");
                Emit("fallback", LLMOrchestrator.HeuristicModelName, "generate-blueprint");
                result    = engine.CompileBlueprint(request.SolutionId, request.SolutionName, request.Domain, request.SubDomain, request.SolutionDescription);
                modelUsed = LLMOrchestrator.HeuristicModelName;
            }
        }
        else
        {
            logger.LogInformation("[Blueprint/Stream] All providers exhausted — using heuristic engine.");
            Emit("fallback", LLMOrchestrator.HeuristicModelName, "generate-blueprint");
            result = engine.CompileBlueprint(request.SolutionId, request.SolutionName, request.Domain, request.SubDomain, request.SolutionDescription);
        }

        // 5. Classify, stamp, cache (mirrors GenerateBlueprintAsync)
        // Stamp user-authored pre-generation context so it grounds documents/chat and feeds classification.
        if (!string.IsNullOrWhiteSpace(request.ProjectNotes))
            result = result with { ProjectNotes = request.ProjectNotes };

        var (solutionType, confidence) = ResolveSolutionType(request.OverrideSolutionType, result);

        var stamped = result with
        {
            ModelUsed              = modelUsed,
            SolutionType           = solutionType,
            SolutionTypeConfidence = confidence,
            SubDomain              = request.SubDomain           ?? result.SubDomain,
            SolutionDescription    = request.SolutionDescription ?? result.SolutionDescription
        };

        // Always cache the blueprint (including heuristic fallback) so DocumentService
        // can retrieve it by ID via the bp-by-id: secondary index when generating documents.
        var ttl = TimeSpan.FromHours(config.GetValue<double>("Cache:Blueprint:TtlHours", 24.0));
        cache.Set(cacheKey, stamped, ttl);
        cache.Set($"bp-by-id:{stamped.Id}", stamped, ttl);

        logger.LogInformation("[Blueprint/Stream] SolutionType: {T} ({C:P0})", solutionType, confidence);
        yield return ("complete", JsonSerializer.Serialize(stamped, JsonOptions));
    }

    /// <summary>
    /// Resolve the solution type by trust order: an explicit caller override (confidence 1.0) →
    /// the generation LLM's own <c>solutionType</c> (canonicalised; server-side so not spoofable) →
    /// the keyword heuristic over the full design. The LLM's confidence is clamped to a sane band;
    /// when it is missing/out of range a solid default is used.
    /// </summary>
    private (string SolutionType, double Confidence) ResolveSolutionType(string? overrideType, SystemBlueprint result)
    {
        if (!string.IsNullOrWhiteSpace(overrideType))
            return (overrideType, 1.0);

        if (classifier.Canonicalize(result.SolutionType) is { } llmType)
        {
            var conf = result.SolutionTypeConfidence is > 0 and <= 1
                ? Math.Clamp(result.SolutionTypeConfidence, 0.5, 0.95)
                : 0.85;
            return (llmType, conf);
        }

        return classifier.Classify(result);
    }

    public Task<SystemBlueprint?> PatchBlueprintAsync(
        string blueprintId, PatchBlueprintRequest patch, CancellationToken ct = default)
    {
        if (!cache.TryGet<SystemBlueprint>($"bp-by-id:{blueprintId}", out var bp))
            return Task.FromResult<SystemBlueprint?>(null);

        var patched = bp with
        {
            ArchDecisions          = patch.ArchDecisions          ?? bp.ArchDecisions,
            QualityAttributes      = patch.QualityAttributes      ?? bp.QualityAttributes,
            TechRadar              = patch.TechRadar              ?? bp.TechRadar,
            CoreScenario           = patch.CoreScenario           ?? bp.CoreScenario,
            BaseTopology           = patch.BaseTopology           ?? bp.BaseTopology,
            DatabaseSchemes        = patch.DatabaseSchemes        ?? bp.DatabaseSchemes,
            EndpointManifest       = patch.EndpointManifest       ?? bp.EndpointManifest,
            ProjectNotes           = patch.ProjectNotes           ?? bp.ProjectNotes,
            BuyVsBuild             = patch.BuyVsBuild             ?? bp.BuyVsBuild,
            Feasibility            = patch.Feasibility            ?? bp.Feasibility,
            // SolutionType/SolutionTypeConfidence carry over from the existing blueprint (see below).
        };

        // The solution type was determined at generation time by the LLM (or an explicit override) and is the
        // trustworthy classification. A section-level patch must NOT silently re-guess the whole system's type
        // via the weaker keyword heuristic — that regressed correct types (e.g. "Batch Processing" flipping to
        // "Event-Driven" after an unrelated edit). So an explicit patch.SolutionType wins at confidence 1.0;
        // otherwise the existing type/confidence is preserved (carried over via the `with` above). The
        // client-supplied SolutionTypeConfidence is still ignored. (Refines ADR-030's re-classify-on-patch now
        // that generation-time classification is LLM-driven; the user can always set an explicit override.)
        if (!string.IsNullOrWhiteSpace(patch.SolutionType))
        {
            var canon = classifier.Canonicalize(patch.SolutionType) ?? patch.SolutionType;
            patched = patched with { SolutionType = canon, SolutionTypeConfidence = 1.0 };
            logger.LogInformation("[Blueprint] SolutionType set on patch — {T}.", canon);
        }

        var ttl      = TimeSpan.FromHours(config.GetValue<double>("Cache:Blueprint:TtlHours", 24.0));
        var cacheKey = cache.ComputeKey(new { patched.SolutionId, patched.SolutionName, patched.Domain });
        cache.Set(cacheKey, patched, ttl);
        cache.Set($"bp-by-id:{patched.Id}", patched, ttl);

        logger.LogInformation("[Blueprint] Patched {Id} — {N} arch decisions.", blueprintId[..8], patched.ArchDecisions.Count);
        return Task.FromResult<SystemBlueprint?>(patched);
    }

    public async IAsyncEnumerable<(string Event, string Data)> RegenerateTopologyAsync(
        string blueprintId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!cache.TryGet<SystemBlueprint>($"bp-by-id:{blueprintId}", out var bp))
        {
            yield return ("error", "Blueprint not found in cache.");
            yield break;
        }

        var (sys, usr) = PromptBuilder.BuildTopologyRegeneration(bp);
        var rawBuilder  = new StringBuilder();

        // Stream from first available provider
        IAsyncEnumerator<string>? active = null;
        string? firstChunk = null;
        var modelUsed = LLMOrchestrator.HeuristicModelName;

        foreach (var provider in _providers)
        {
            if (!provider.IsConfigured) continue;
            var enumerator = provider.StreamAsync(sys, usr, ct).GetAsyncEnumerator(ct);
            bool hasFirst;
            try { hasFirst = await enumerator.MoveNextAsync(); }
            catch { await enumerator.DisposeAsync(); continue; }
            if (!hasFirst) { await enumerator.DisposeAsync(); continue; }
            active = enumerator; firstChunk = enumerator.Current; modelUsed = provider.Name; break;
        }

        if (active is not null && firstChunk is not null)
        {
            rawBuilder.Append(firstChunk);
            yield return ("chunk", firstChunk);
            await using (active)
            {
                while (await active.MoveNextAsync())
                {
                    rawBuilder.Append(active.Current);
                    yield return ("chunk", active.Current);
                }
            }
        }
        else
        {
            // Heuristic fallback — use domain-specific local topology
            yield return ("chunk", bp.BaseTopology);
            rawBuilder.Append(bp.BaseTopology);
        }

        // Extract the topology — the response is Markdown with a code fence
        var raw = rawBuilder.ToString().Trim();
        var fenceMatch = System.Text.RegularExpressions.Regex.Match(
            raw, @"```[^\n]*\n([\s\S]*?)```", System.Text.RegularExpressions.RegexOptions.Singleline);
        var newTopology = fenceMatch.Success ? $"## Base Topology\n\n```\n{fenceMatch.Groups[1].Value.Trim()}\n```" : raw;

        var patched = bp with { BaseTopology = newTopology, ModelUsed = modelUsed };
        var ttl     = TimeSpan.FromHours(config.GetValue<double>("Cache:Blueprint:TtlHours", 24.0));
        var cacheKey = cache.ComputeKey(new { patched.SolutionId, patched.SolutionName, patched.Domain });
        cache.Set(cacheKey, patched, ttl);
        cache.Set($"bp-by-id:{patched.Id}", patched, ttl);

        logger.LogInformation("[Blueprint] Topology regenerated for {Id} via {M}.", blueprintId[..8], modelUsed);
        yield return ("complete", JsonSerializer.Serialize(patched, JsonOptions));
    }
}
