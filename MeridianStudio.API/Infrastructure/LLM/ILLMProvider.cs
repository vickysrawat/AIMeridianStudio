using System.Runtime.CompilerServices;

namespace MeridianStudio.API.Infrastructure.LLM;

/// <summary>
/// Contract for a single LLM backend. Register implementations in PRIORITY ORDER
/// (Gemini → Groq → Claude). The orchestrator tries each configured provider in
/// registration sequence and falls back to the local heuristic engine on failure.
/// </summary>
public interface ILLMProvider
{
    /// <summary>Display name used in logs (e.g. "Gemini 2.5 Flash").</summary>
    string Name { get; }

    /// <summary>
    /// True when an API key is present in configuration.
    /// Providers without a key are skipped by the orchestrator.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Conservative upper bound on input tokens this provider accepts. Used by the
    /// prompt-context budget allocator (Phase 3) to size the assembled prompt to the
    /// provider actually serving the call. Defaults to a safe floor; large-context
    /// providers override it.
    /// </summary>
    int MaxInputTokens => 120_000;

    /// <summary>
    /// Send a prompt pair and return the raw text completion.
    /// Implementations must surface HTTP 429 / 503 as
    /// <see cref="System.Net.Http.HttpRequestException"/> with the matching
    /// <see cref="System.Net.HttpStatusCode"/> so the orchestrator can log and rotate.
    /// </summary>
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);

    /// <summary>
    /// Stream the completion as raw text chunks.
    /// Default implementation wraps <see cref="CompleteAsync"/> as a single chunk.
    /// Override in providers that support native SSE streaming.
    /// </summary>
    async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return await CompleteAsync(systemPrompt, userPrompt, ct);
    }
}
