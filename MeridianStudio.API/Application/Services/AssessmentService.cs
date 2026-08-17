using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.LLM.Embedding;
using MeridianStudio.API.Infrastructure.LLM.Providers;
using MeridianStudio.API.Infrastructure.LocalEngine;
using MeridianStudio.API.Infrastructure.Realtime;
using MeridianStudio.API.Infrastructure.WebSearch;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Generates standalone, use-case-shaped Assessments (Use Case workflow). Mirrors the
/// blueprint streaming pipeline — provider cascade, heuristic fallback, caching — but
/// produces an <see cref="Assessment"/> cached under "assess-by-id:{id}" so the document
/// pipeline can ground documents in it.
/// </summary>
public sealed class AssessmentService(
    PayloadCache cache,
    IEnumerable<ILLMProvider> providers,
    LocalCompilationEngine engine,
    LLMOrchestrator orchestrator,
    WebResearchEnricher enricher,
    IDomainClassifier domainClassifier,
    IConfiguration config,
    ILogger<AssessmentService> logger) : IAssessmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented          = false
    };

    private readonly IReadOnlyList<ILLMProvider> _providers = [.. providers];

    public async IAsyncEnumerable<(string Event, string Data)> StreamAssessmentAsync(
        AssessmentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // ── Search-first grounding: fetch real evidence BEFORE the LLM synthesises ──────────
        // Runs only when the caller opted in and a web-search provider is configured. Status
        // events keep the UI informed during the pre-step; the assessment then streams grounded
        // in the fetched sources (cited as [S#]).
        LiveResearchContext? live = null;
        if (request.GroundInLiveResearch && enricher.IsLiveSearchAvailable)
        {
            yield return ("status", "Extracting search queries…");
            var extraction = await ExtractUseCaseQueriesAsync(request, ct);

            yield return ("status", "Gathering live evidence…");
            live = await SafeEnrichAsync(extraction, ct);

            if (live.HasData)
                yield return ("sources", JsonSerializer.Serialize(live.Results, JsonOptions));
        }

        var (sys, usr) = PromptBuilder.BuildAssessment(request, live);
        var rawBuilder = new StringBuilder();
        var modelUsed  = LLMOrchestrator.HeuristicModelName;
        var streamOk   = false;
        string? lastError = null;

        IAsyncEnumerator<string>? active = null;
        string? firstChunk = null;

        foreach (var provider in _providers)
        {
            if (!provider.IsConfigured) continue;

            Emit("attempting", provider.Name);
            var enumerator = provider.StreamAsync(sys, usr, ct).GetAsyncEnumerator(ct);
            bool hasFirst;
            try { hasFirst = await enumerator.MoveNextAsync(); }
            catch (Exception ex)
            {
                lastError = Describe(ex);
                logger.LogWarning(ex, "[Assessment/Stream] {P} failed before first chunk ({Reason}) — trying next.",
                    provider.Name, lastError);
                Emit("failed", provider.Name, lastError);
                await enumerator.DisposeAsync();
                continue;
            }

            if (!hasFirst)
            {
                Emit("failed", provider.Name);
                await enumerator.DisposeAsync();
                continue;
            }

            active     = enumerator;
            firstChunk = enumerator.Current;
            modelUsed  = provider.Name;
            streamOk   = true;
            Emit("succeeded", provider.Name);
            break;
        }

        if (streamOk && active is not null && firstChunk is not null)
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

        Assessment result;
        if (streamOk && rawBuilder.Length > 0)
        {
            try
            {
                var rawText = rawBuilder.ToString();
                var jStart  = rawText.IndexOf('{');
                var jEnd    = rawText.LastIndexOf('}');
                var toParse = jStart >= 0 && jEnd > jStart ? rawText[jStart..(jEnd + 1)] : rawText;
                result = LLMResponseParser.ParseAssessment(toParse, request);
            }
            catch (Exception ex)
            {
                var preview = rawBuilder.Length > 0
                    ? rawBuilder.ToString()[..Math.Min(400, rawBuilder.Length)]
                    : "(empty)";
                logger.LogWarning(ex,
                    "[Assessment/Stream] Parse failed after {Len} chars — falling back to engine. " +
                    "First 400 chars of assembled text: {Preview}",
                    rawBuilder.Length, preview);
                result    = CompileHeuristic(request);
                modelUsed = LLMOrchestrator.HeuristicModelName;
                Emit("fallback", LLMOrchestrator.HeuristicModelName);
            }
        }
        else
        {
            var reason = lastError ?? "No live AI provider is configured or reachable.";
            logger.LogInformation("[Assessment/Stream] All providers exhausted ({Reason}) — using heuristic engine.", reason);
            // Surface the reason inline so the UI can show WHY the offline engine was used.
            yield return ("notice", $"Live AI unavailable — {reason} Generated with the offline engine.");
            Emit("fallback", LLMOrchestrator.HeuristicModelName, reason);
            result = CompileHeuristic(request);
        }

        var stamped = result with { ModelUsed = modelUsed };
        var ttl     = TimeSpan.FromHours(config.GetValue<double>("Cache:Blueprint:TtlHours", 24.0));
        cache.Set($"assess-by-id:{stamped.Id}", stamped, ttl);

        logger.LogInformation("[Assessment/Stream] Done — {Id} via {M}.", stamped.Id, modelUsed);
        yield return ("complete", JsonSerializer.Serialize(stamped, JsonOptions));
    }

    // ── Search-first helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Extracts domain + focused search queries from the brief via the LLM (Gemini-first),
    /// falling back to a deterministic heuristic when the model is offline or returns nothing
    /// usable — so live search always has queries to run.
    /// </summary>
    private async Task<UseCaseExtraction> ExtractUseCaseQueriesAsync(
        AssessmentRequest request, CancellationToken ct)
    {
        var domain = request.Domain ?? string.Empty;
        if (string.IsNullOrWhiteSpace(domain))
        {
            try
            {
                var cls = await domainClassifier.ClassifyAsync(
                    $"{request.UseCase} {request.Objective} {request.ProblemStatement} {request.UseCaseScenario}", ct);
                domain = cls.Domain;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Assessment] Domain classification failed — proceeding without it.");
            }
        }

        UseCaseExtraction Heuristic() => UseCaseQueryBuilder.From(request, domain, string.Empty);

        try
        {
            var (extraction, _) = await orchestrator.ExecuteAsync(
                "usecase-extraction",
                async (provider, pCt) =>
                {
                    var (sys, usr) = PromptBuilder.BuildUseCaseExtraction(request);
                    var raw = await provider.CompleteAsync(sys, usr, pCt);
                    return LLMResponseParser.ParseUseCaseExtraction(raw, request);
                },
                Heuristic,
                ct);

            return string.IsNullOrWhiteSpace(extraction.CoreQuery) ? Heuristic() : extraction;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Assessment] Query extraction failed — using heuristic fallback.");
            return Heuristic();
        }
    }

    /// <summary>Runs use-case live search, degrading to empty context on any failure.</summary>
    private async Task<LiveResearchContext> SafeEnrichAsync(UseCaseExtraction extraction, CancellationToken ct)
    {
        try
        {
            var live = await enricher.EnrichUseCaseAsync(extraction, ct);
            logger.LogInformation("[Assessment] live evidence: {N} source(s) via {P}.",
                live.Results.Count, string.Join(", ", live.SourcesQueried));
            return live;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Assessment] Live enrichment failed — continuing without live data.");
            return LiveResearchContext.Empty;
        }
    }

    public Task<Assessment?> PatchAssessmentAsync(
        string assessmentId, PatchAssessmentRequest patch, CancellationToken ct = default)
    {
        if (!cache.TryGet<Assessment>($"assess-by-id:{assessmentId}", out var a))
            return Task.FromResult<Assessment?>(null);

        var patched = a with
        {
            ExecutiveSummary     = patch.ExecutiveSummary     ?? a.ExecutiveSummary,
            Sections             = patch.Sections             ?? a.Sections,
            Recommendations      = patch.Recommendations      ?? a.Recommendations,
            Risks                = patch.Risks                ?? a.Risks,
            NextSteps            = patch.NextSteps            ?? a.NextSteps,
            Feasibility          = patch.Feasibility          ?? a.Feasibility,
            RecommendedDocuments = patch.RecommendedDocuments ?? a.RecommendedDocuments,
        };

        var ttl = TimeSpan.FromHours(config.GetValue<double>("Cache:Blueprint:TtlHours", 24.0));
        cache.Set($"assess-by-id:{patched.Id}", patched, ttl);
        logger.LogInformation("[Assessment] Patched {Id}.", assessmentId[..Math.Min(8, assessmentId.Length)]);
        return Task.FromResult<Assessment?>(patched);
    }

    public async IAsyncEnumerable<(string Event, string Data)> StreamChatAsync(
        string assessmentId,
        BlueprintChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!cache.TryGet<Assessment>($"assess-by-id:{assessmentId}", out var a))
        {
            yield return ("error", "Assessment not found. Please generate it first.");
            yield break;
        }

        var (sys, usr) = PromptBuilder.BuildAssessmentChat(a, request);
        var rawBuilder = new StringBuilder();

        IAsyncEnumerator<string>? active = null;
        string? firstChunk = null;

        foreach (var provider in _providers)
        {
            if (!provider.IsConfigured) continue;
            var en = provider.StreamAsync(sys, usr, ct).GetAsyncEnumerator(ct);
            bool hasFirst;
            try { hasFirst = await en.MoveNextAsync(); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Assessment/Chat] {P} failed before first chunk.", provider.Name);
                await en.DisposeAsync();
                continue;
            }
            if (!hasFirst) { await en.DisposeAsync(); continue; }
            active = en; firstChunk = en.Current; break;
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
            yield return ("chunk", "I'm currently offline. Please ensure an API key is configured.");
        }

        var full       = rawBuilder.ToString();
        var applyMatch = Regex.Match(full, @"<apply>([\s\S]*?)</apply>", RegexOptions.Singleline);
        if (applyMatch.Success)
        {
            var rawApply = applyMatch.Groups[1].Value.Trim();
            string compact;
            try
            {
                using var d = JsonDocument.Parse(rawApply);
                compact = JsonSerializer.Serialize(d.RootElement);
            }
            catch { compact = rawApply; }
            yield return ("apply", compact);
        }

        yield return ("done", "{}");
    }

    private Assessment CompileHeuristic(AssessmentRequest r) =>
        engine.CompileAssessment(
            r.UseCaseScenario, r.UseCase, r.Context, r.ProblemStatement,
            r.Objective, r.ScopeOfWork, r.ExpectedOutcome, r.Domain);

    private void Emit(string type, string provider, string? detail = null) =>
        orchestrator.RecordStatus(type, provider, "assessment", detail);

    /// <summary>Turns a provider exception into a short, user-facing reason.</summary>
    private static string Describe(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.HostNotFound })
                return "network/DNS unreachable — the AI host could not be resolved (offline?).";
            if (e is TaskCanceledException or TimeoutException)
                return "the request timed out.";
            if (e is System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests })
                return "rate limit reached (HTTP 429).";
        }
        var msg = ex.Message.Split('\n')[0].Trim();
        return msg.Length > 160 ? msg[..160] + "…" : msg;
    }
}
