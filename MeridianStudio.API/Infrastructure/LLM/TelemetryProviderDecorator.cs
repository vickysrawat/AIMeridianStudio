using System.Diagnostics;
using System.Runtime.CompilerServices;
using MeridianStudio.API.Infrastructure.Telemetry;
using MeridianStudio.API.Infrastructure.Tokenization;

namespace MeridianStudio.API.Infrastructure.LLM;

/// <summary>
/// Non-invasive <see cref="ILLMProvider"/> wrapper that measures token usage, estimated
/// cost, latency and outcome of every <see cref="CompleteAsync"/> call, attributing it to
/// the ambient operation (<see cref="LlmOperationContext"/>) set by the orchestrator.
/// Input tokens are counted from the system + user prompts; output tokens from the raw
/// completion. <see cref="StreamAsync"/> is delegated straight through so native SSE
/// streaming (e.g. Gemini) is preserved; streaming calls are not metered in this phase.
/// </summary>
public sealed class TelemetryProviderDecorator(
    ILLMProvider inner,
    ITokenCounter tokens,
    ILlmTelemetry telemetry) : ILLMProvider
{
    public string Name => inner.Name;

    public bool IsConfigured => inner.IsConfigured;

    public int MaxInputTokens => inner.MaxInputTokens;

    public async Task<string> CompleteAsync(
        string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var operation   = LlmOperationContext.Current;
        var estInput    = tokens.Count(systemPrompt) + tokens.Count(userPrompt);
        LlmUsageScope.Current = null;   // clear any stale value before the call
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await inner.CompleteAsync(systemPrompt, userPrompt, ct);
            sw.Stop();

            // Prefer the provider's ACTUAL usage (incl. cache-read tokens) when it reported it;
            // otherwise fall back to local proxy token counts.
            var usage = LlmUsageScope.Current;
            var inputTokens  = usage?.InputTokens  ?? estInput;
            var outputTokens = usage?.OutputTokens ?? tokens.Count(result);
            var cachedInput  = usage?.CachedInputTokens ?? 0;
            Record(operation, inputTokens, outputTokens, cachedInput, sw, success: true);
            return result;
        }
        catch
        {
            sw.Stop();
            // A failed attempt still consumed input tokens before the provider rejected it.
            Record(operation, estInput, outputTokens: 0, cachedInput: 0, sw, success: false);
            throw; // propagate so the orchestrator rotates to the next provider
        }
        finally
        {
            LlmUsageScope.Current = null;
        }
    }

    // Delegated unmetered so providers that override StreamAsync keep native streaming.
    public IAsyncEnumerable<string> StreamAsync(
        string systemPrompt, string userPrompt, CancellationToken ct = default)
        => inner.StreamAsync(systemPrompt, userPrompt, ct);

    private void Record(string operation, int inputTokens, int outputTokens, int cachedInput, Stopwatch sw, bool success) =>
        telemetry.Record(new LlmCallRecord(
            inner.Name, operation, inputTokens, outputTokens,
            telemetry.EstimateCostUsd(inner.Name, inputTokens, outputTokens, cachedInput),
            sw.ElapsedMilliseconds, success, DateTimeOffset.UtcNow, cachedInput));
}
