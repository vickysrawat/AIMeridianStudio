using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MeridianStudio.API.Infrastructure.LLM.Providers;

/// <summary>
/// Claude Sonnet provider via the Anthropic Messages API.
/// Uses the assistant-prefill technique (seeding the assistant turn with "{")
/// to reliably force pure JSON output without any preamble.
/// Named HttpClient: "Claude".
/// </summary>
public sealed class ClaudeProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<ClaudeProvider> logger) : ILLMProvider
{
    private const string DefaultModel     = "claude-sonnet-4-6";
    private const string Endpoint         = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private string Model => config["LLM:Claude:Model"] ?? DefaultModel;

    public string Name => $"Claude ({Model})";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(config["LLM:Claude:ApiKey"]);

    // Claude Sonnet 4.x advertises a ~200K-token context window; reserve headroom for output.
    public int MaxInputTokens => 190_000;

    public async Task<string> CompleteAsync(
        string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var apiKey = config["LLM:Claude:ApiKey"]
            ?? throw new InvalidOperationException("LLM:Claude:ApiKey is not set.");

        // Prefill assistant turn with "{" — Claude continues from this prefix,
        // guaranteeing the response starts as a JSON object (no preamble text).
        //
        // Prompt caching (B1): the system prompt is split into up to TWO cached blocks —
        //   1. the request-independent StableSystemPreamble (identical across ALL requests →
        //      cross-request cache hits), and
        //   2. the per-request remainder (persona + task rules → hits across a doc's multi-pass loop).
        // Both carry cache_control: ephemeral. Anthropic caches by prefix (5-min TTL) and serves
        // repeats at the discounted cache-read rate. Below the per-model minimum cacheable size the
        // markers are simply ignored — harmless.
        var body = new
        {
            model      = Model,
            max_tokens = 8192,
            system     = BuildCachedSystemBlocks(systemPrompt),
            messages   = new[]
            {
                new { role = "user",      content = userPrompt },
                new { role = "assistant", content = "{"        }  // JSON prefill
            }
        };

        var client = httpClientFactory.CreateClient("Claude");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("x-api-key",          apiKey);
        request.Headers.Add("anthropic-version",  AnthropicVersion);
        request.Content = JsonContent.Create(body);

        logger.LogDebug("[Claude] POST {Endpoint} model={Model}", Endpoint, Model);

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();   // surfaces 429/503 as HttpRequestException

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct)
            ?? throw new InvalidOperationException("Claude returned empty body.");

        ReportUsage(doc.RootElement);

        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Claude returned empty text.");

        // The prefill "{" is NOT included in the completion — prepend it back
        // so the parser receives a complete JSON object.
        return text.TrimStart().StartsWith('{') ? text : "{" + text;
    }

    /// <summary>
    /// Reports actual token usage (incl. cache-read tokens) to the telemetry side-channel so B1
    /// caching savings are measurable. Anthropic's <c>input_tokens</c> excludes cached tokens, so
    /// full input = input_tokens + cache_read + cache_creation.
    /// </summary>
    private static void ReportUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u)) return;

        int input  = GetInt(u, "input_tokens");
        int output = GetInt(u, "output_tokens");
        int read   = GetInt(u, "cache_read_input_tokens");
        int create = GetInt(u, "cache_creation_input_tokens");

        MeridianStudio.API.Infrastructure.Telemetry.LlmUsageScope.Current =
            new MeridianStudio.API.Infrastructure.Telemetry.LlmCallUsage(
                InputTokens: input + read + create,
                OutputTokens: output,
                CachedInputTokens: read);
    }

    private static int GetInt(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;

    /// <summary>
    /// Splits the system prompt into cached blocks. If it leads with the shared
    /// <see cref="PromptBuilder.StableSystemPreamble"/>, emit that as its own cached block (so it
    /// hits across different requests) followed by the per-request remainder as a second cached
    /// block. Otherwise fall back to a single cached block. Empty segments are omitted.
    /// </summary>
    private static object[] BuildCachedSystemBlocks(string systemPrompt)
    {
        static object Block(string text) => new { type = "text", text, cache_control = new { type = "ephemeral" } };

        var preamble = PromptBuilder.StableSystemPreamble;
        if (systemPrompt.StartsWith(preamble, StringComparison.Ordinal))
        {
            var remainder = systemPrompt[preamble.Length..].TrimStart('\n', '\r');
            return remainder.Length == 0
                ? [Block(preamble)]
                : [Block(preamble), Block(remainder)];
        }

        return [Block(systemPrompt)];
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt, string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = config["LLM:Claude:ApiKey"]
            ?? throw new InvalidOperationException("LLM:Claude:ApiKey is not set.");

        // No prefill in streaming mode — assistant-prefill is incompatible with stream: true.
        var body = new
        {
            model      = Model,
            max_tokens = 8192,
            system     = systemPrompt,
            stream     = true,
            messages   = new[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        var client = httpClientFactory.CreateClient("Claude");

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        req.Headers.Add("x-api-key",         apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);
        req.Content = JsonContent.Create(body);

        logger.LogDebug("[Claude] Stream POST {Endpoint} model={Model}", Endpoint, Model);

        var response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? currentEvent = null;

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
        {
            if (line.StartsWith("event: "))
            {
                currentEvent = line[7..].Trim();
                if (currentEvent is "message_stop") break;
                continue;
            }

            if (!line.StartsWith("data: ")) continue;
            if (currentEvent is not "content_block_delta") continue;

            var data = line[6..];
            string? text = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var delta = doc.RootElement.GetProperty("delta");
                if (!delta.TryGetProperty("text", out var textEl)) continue;
                text = textEl.GetString();
            }
            catch { /* malformed chunk — skip */ }

            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }
}
