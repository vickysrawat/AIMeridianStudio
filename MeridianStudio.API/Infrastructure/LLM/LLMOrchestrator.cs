using System.Collections.Concurrent;
using System.Net;
using MeridianStudio.API.Infrastructure.Realtime;
using MeridianStudio.API.Infrastructure.Telemetry;
using MeridianStudio.API.Infrastructure.Tokenization;

namespace MeridianStudio.API.Infrastructure.LLM;

/// <summary>
/// Routes each LLM operation through the priority chain:
///   Gemini 2.5 Flash → Groq LLaMA 3.3 70B → Claude Sonnet → Local Heuristic Engine
///
/// Only providers with a configured API key are attempted.
/// On HTTP 429 / 503 the canonical resilience log line is emitted and the next
/// provider is tried. Any other exception also rotates to the next provider.
/// If all configured providers fail the local heuristic fallback is invoked.
/// Register as Singleton.
/// </summary>
public sealed class LLMOrchestrator(
    IEnumerable<ILLMProvider> providers,
    ModelStatusBroadcaster broadcaster,
    ITokenCounter tokens,
    ILlmTelemetry telemetry,
    IConfiguration config,
    ILogger<LLMOrchestrator> logger)
{
    private const string QuotaLog =
        "[Resilience Router] External API quota limit reached. " +
        "Route safely diverted to localized compilation engine.";

    // Order is determined by DI registration order (Gemini, Groq, Claude).
    // Each provider is wrapped so every call is metered for cost/token telemetry;
    // the decorator passes Name/IsConfigured through and delegates native streaming.
    private readonly IReadOnlyList<ILLMProvider> _chain =
        [.. providers.Select(p => new TelemetryProviderDecorator(p, tokens, telemetry))];

    internal const string HeuristicModelName = "Heuristic Engine (Offline)";

    // Per-provider runtime state — updated on every succeeded/failed/quota/fallback event.
    private readonly ConcurrentDictionary<string, (string Status, string Reason)> _runtimeStates = new();

    /// <summary>
    /// Returns the configuration and last-known runtime state for every provider
    /// in priority order, plus the heuristic engine entry at the end.
    /// </summary>
    public IReadOnlyList<ProviderStatusItem> GetProviderStatuses()
    {
        var items = new List<ProviderStatusItem>(_chain.Count + 1);

        for (int i = 0; i < _chain.Count; i++)
        {
            var p = _chain[i];
            if (!p.IsConfigured)
            {
                items.Add(new ProviderStatusItem(
                    p.Name, i + 1, Configured: false,
                    Status: "not-configured",
                    Reason: "No API key configured. Set one with: " +
                            $"dotnet user-secrets set \"LLM:{ProviderKey(p.Name)}:ApiKey\" \"<key>\""));
            }
            else if (_runtimeStates.TryGetValue(p.Name, out var state))
            {
                items.Add(new ProviderStatusItem(p.Name, i + 1, Configured: true, state.Status, state.Reason));
            }
            else
            {
                items.Add(new ProviderStatusItem(
                    p.Name, i + 1, Configured: true,
                    Status: "idle",
                    Reason: "API key is configured and ready — no calls made yet this session."));
            }
        }

        // Heuristic engine is always last
        if (_runtimeStates.TryGetValue(HeuristicModelName, out var hState))
            items.Add(new ProviderStatusItem(HeuristicModelName, _chain.Count + 1, Configured: true, hState.Status, hState.Reason));
        else
            items.Add(new ProviderStatusItem(
                HeuristicModelName, _chain.Count + 1, Configured: true,
                Status: "idle",
                Reason: "Always available — invoked automatically when all external providers fail or are unconfigured."));

        return items;
    }

    /// <summary>
    /// Executes <paramref name="operation"/> against each configured provider in
    /// priority order. Falls back to <paramref name="heuristicFallback"/> if all
    /// providers fail or none are configured.
    /// Returns the result together with the name of the model/engine that produced it.
    /// </summary>
    public async Task<(T Result, string ModelUsed)> ExecuteAsync<T>(
        string operationName,
        Func<ILLMProvider, CancellationToken, Task<T>> operation,
        Func<T> heuristicFallback,
        CancellationToken ct = default)
    {
        var (result, model, _) = await ExecuteWithTraceAsync(operationName, operation, heuristicFallback, ct);
        return (result, model);
    }

    /// <summary>
    /// Same as <see cref="ExecuteAsync"/> but also returns the ordered list of providers attempted
    /// (each with its outcome) so callers can build <see cref="MeridianStudio.API.Domain.Models.OutputProvenance"/>.
    /// The 2-tuple <see cref="ExecuteAsync"/> delegates here, so existing callers are unaffected.
    /// </summary>
    public async Task<(T Result, string ModelUsed, IReadOnlyList<string> ProvidersAttempted)> ExecuteWithTraceAsync<T>(
        string operationName,
        Func<ILLMProvider, CancellationToken, Task<T>> operation,
        Func<T> heuristicFallback,
        CancellationToken ct = default)
    {
        // Attribute every metered provider call in this flow to the current operation.
        LlmOperationContext.Current = operationName;

        var attempted = new List<string>();
        var active = ApplyRoutingProfile(operationName, _chain.Where(p => p.IsConfigured).ToList());

        if (active.Count == 0)
        {
            logger.LogInformation(
                "[LLM Orchestrator] No providers configured (or heuristic-eligible routing) — using heuristic engine for '{Op}'.",
                operationName);
            _runtimeStates[HeuristicModelName] = (
                "fallback",
                "No external providers are configured. Running fully offline.");
            Emit("fallback", HeuristicModelName, operationName);
            return (heuristicFallback(), HeuristicModelName, attempted);
        }

        foreach (var provider in active)
        {
            ct.ThrowIfCancellationRequested();
            attempted.Add(provider.Name);

            try
            {
                logger.LogInformation(
                    "[LLM Orchestrator] → {Provider} | op: '{Op}'",
                    provider.Name, operationName);

                Emit("attempting", provider.Name, operationName);

                var result = await operation(provider, ct);

                logger.LogInformation(
                    "[LLM Orchestrator] ✓ {Provider} succeeded for '{Op}'.",
                    provider.Name, operationName);

                _runtimeStates[provider.Name] = (
                    "active",
                    $"Last call succeeded (operation: {operationName}).");
                Emit("succeeded", provider.Name, operationName);
                return (result, provider.Name, attempted);
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is HttpStatusCode.TooManyRequests
                                    or HttpStatusCode.ServiceUnavailable)
            {
                if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    logger.LogWarning(QuotaLog);
                    _runtimeStates[provider.Name] = (
                        "quota",
                        "Rate limit reached (HTTP 429) — try again later, or check your plan limits.");
                }
                else
                {
                    logger.LogWarning(
                        "[LLM Orchestrator] {Provider} returned HTTP 503 for '{Op}' — service temporarily unavailable.",
                        provider.Name, operationName);
                    _runtimeStates[provider.Name] = (
                        "unavailable",
                        "Service unavailable (HTTP 503) — the provider's API is temporarily down or the model name is incorrect.");
                }
                Emit("failed", provider.Name, operationName);
                // rotate to next provider
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // genuine user / request cancellation — do not rotate
            }
            catch (OperationCanceledException ex)
            {
                // Provider-side HttpClient timeout — treat as a provider failure and rotate
                logger.LogWarning(
                    ex,
                    "[LLM Orchestrator] {Provider} timed out for '{Op}' — trying next provider.",
                    provider.Name, operationName);
                _runtimeStates[provider.Name] = ("failed", "Request timed out — the provider took too long to respond.");
                Emit("failed", provider.Name, operationName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "[LLM Orchestrator] {Provider} failed for '{Op}' — trying next provider.",
                    provider.Name, operationName);
                var msg = ex.Message.Split('\n')[0].Trim();
                if (msg.Length > 120) msg = msg[..120] + "…";
                _runtimeStates[provider.Name] = ("failed", $"Call failed: {msg}");
                Emit("failed", provider.Name, operationName);
                // rotate to next provider
            }
        }

        logger.LogWarning(
            "[LLM Orchestrator] All {Count} provider(s) exhausted for '{Op}'. " +
            "Diverting to heuristic engine.",
            active.Count, operationName);

        _runtimeStates[HeuristicModelName] = (
            "fallback",
            $"All {active.Count} external provider(s) failed. Running offline.");
        Emit("fallback", HeuristicModelName, operationName);
        return (heuristicFallback(), HeuristicModelName, attempted);
    }

    /// <summary>
    /// Records a provider's runtime state and broadcasts the matching status event.
    /// For streaming services (assessment, blueprint) that run their own provider
    /// cascade outside <see cref="ExecuteAsync"/> — without this, their successful
    /// calls never reach <c>_runtimeStates</c>, so <c>/providers/status</c> keeps
    /// reporting "no calls made yet" even after a live model produced the result.
    /// Mirrors ExecuteAsync: only terminal events mutate state; "attempting" just
    /// broadcasts.
    /// </summary>
    public void RecordStatus(string eventType, string provider, string operation, string? detail = null)
    {
        switch (eventType)
        {
            case "succeeded":
                _runtimeStates[provider] = ("active", $"Last call succeeded (operation: {operation}).");
                break;
            case "failed":
                _runtimeStates[provider] = ("failed", detail ?? $"Last call failed (operation: {operation}).");
                break;
            case "quota":
                _runtimeStates[provider] = ("quota", "Rate limit reached (HTTP 429) — try again later, or check your plan limits.");
                break;
            case "fallback":
                _runtimeStates[provider] = ("fallback", detail ?? "All external providers failed or are unconfigured. Running offline.");
                break;
            // "attempting" (and any other transient event): broadcast only, no state change.
        }
        Emit(eventType, provider, operation, detail);
    }

    /// <summary>
    /// Complexity-aware routing (B2). Reorders/filters the configured chain per operation, driven by
    /// config <c>Routing:Profiles:&lt;operation&gt;</c>. Default (no config) preserves the fixed
    /// cost-optimized order, so the fallback contract is unchanged.
    ///   • "quality-first"      → try the highest-quality provider first (Claude), then the rest.
    ///   • "heuristic-eligible" → skip external providers entirely (return empty → heuristic engine).
    ///   • "cost-optimized"/absent → unchanged.
    /// </summary>
    private List<ILLMProvider> ApplyRoutingProfile(string operationName, List<ILLMProvider> active)
    {
        var profile = config[$"Routing:Profiles:{operationName}"]?.Trim().ToLowerInvariant();
        switch (profile)
        {
            case "heuristic-eligible":
                logger.LogInformation("[LLM Orchestrator] Routing '{Op}' straight to heuristic engine (profile).", operationName);
                return [];
            case "quality-first":
                return [.. active.OrderByDescending(p => p.Name.Contains("Claude", StringComparison.OrdinalIgnoreCase))];
            default:
                return active; // cost-optimized / unknown → unchanged
        }
    }

    private void Emit(string type, string provider, string operation, string? detail = null) =>
        broadcaster.Broadcast(new ModelStatusEvent(type, provider, operation, DateTimeOffset.UtcNow, detail));

    // Extracts the config key segment from a provider display name.
    // "Gemini (gemini-2.5-flash)" → "Gemini"
    private static string ProviderKey(string name) =>
        name.Split(' ')[0];
}
