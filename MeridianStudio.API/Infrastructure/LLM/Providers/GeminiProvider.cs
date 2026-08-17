using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MeridianStudio.API.Infrastructure.LLM.Providers;

/// <summary>
/// Gemini 2.5 Flash provider.
/// Uses <c>responseMimeType: "application/json"</c> to enforce structured output
/// so the response is pure JSON without markdown fences.
/// Named HttpClient: "Gemini".
/// </summary>
public sealed class GeminiProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<GeminiProvider> logger) : ILLMProvider
{
    private const string DefaultModel = "gemini-2.5-flash";
    private const string BaseUrl      = "https://generativelanguage.googleapis.com/v1beta/models";

    private string Model => config["LLM:Gemini:Model"] ?? DefaultModel;

    public string Name => $"Gemini ({Model})";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(config["LLM:Gemini:ApiKey"]);

    // Gemini 2.5 Flash advertises a ~1M-token context window.
    public int MaxInputTokens => 1_000_000;

    private string LiteModel => config["LLM:Gemini:LiteModel"] ?? "gemini-2.5-flash-lite";

    // System-prompt → explicit context cache (name + expiry). Singleton-scoped, so it
    // survives across calls. Only used when LLM:Gemini:ExplicitCaching is enabled.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Name, DateTimeOffset Expiry)> _systemCache = new();

    public async Task<string> CompleteAsync(
        string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var apiKey = config["LLM:Gemini:ApiKey"]
            ?? throw new InvalidOperationException("LLM:Gemini:ApiKey is not set.");

        // OPT-IN explicit context caching (default off). Gemini 2.5 already does *implicit*
        // caching automatically; this is only worthwhile for very large, stable system prompts.
        // Implemented as a try-first wrapper: on ANY failure it falls through to the proven
        // inline path below, which keeps the default behaviour completely unchanged.
        if (config.GetValue("LLM:Gemini:ExplicitCaching", false)
            && systemPrompt.Length >= config.GetValue("LLM:Gemini:CacheMinChars", 4096))
        {
            try
            {
                var cachedName = await EnsureCachedSystemAsync(systemPrompt, apiKey, ct);
                if (cachedName is not null)
                    return await SendCachedAsync(cachedName, userPrompt, apiKey, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "[Gemini] Explicit context caching failed — falling back to the inline system prompt.");
            }
        }

        var body = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = userPrompt } } }
            },
            generationConfig = new
            {
                temperature      = 0.2,
                maxOutputTokens  = 32768,
                responseMimeType = "application/json",
                thinkingConfig   = new { thinkingBudget = 0 }
            }
        };

        try
        {
            return await SendCompleteAsync(Model, body, apiKey, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            // Primary model is temporarily overloaded (HTTP 503) — retry once with the
            // lite variant before letting the orchestrator rotate to Groq.
            logger.LogWarning(
                "[Gemini] {Model} returned HTTP 503 — retrying with lite model {Lite}.",
                Model, LiteModel);
            return await SendCompleteAsync(LiteModel, body, apiKey, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null && ex.InnerException is IOException)
        {
            // Connection-level failure: Gemini dropped the TCP connection before the
            // response completed (ResponseEnded / premature close). Retry once with the
            // lite model which produces shorter responses and is less likely to hit the
            // connection limit.
            logger.LogWarning(
                "[Gemini] {Model} connection dropped ({Msg}) — retrying with lite model {Lite}.",
                Model, ex.InnerException.Message, LiteModel);
            return await SendCompleteAsync(LiteModel, body, apiKey, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The HttpClient's own 90-second timeout fired (not a user/request cancellation).
            // Retry once with the lite model before letting the orchestrator rotate to Groq.
            logger.LogWarning(
                "[Gemini] {Model} timed out — retrying with lite model {Lite}.",
                Model, LiteModel);
            return await SendCompleteAsync(LiteModel, body, apiKey, ct);
        }
    }

    // ── Explicit context caching (opt-in) ─────────────────────────────────────

    /// <summary>
    /// Returns the name of a Gemini cachedContents resource holding <paramref name="systemPrompt"/>
    /// as the system instruction, creating one (with a TTL) if absent or expired. Cached in-memory
    /// by content hash so repeated calls with the same system prompt reuse the same resource.
    /// </summary>
    private async Task<string?> EnsureCachedSystemAsync(string systemPrompt, string apiKey, CancellationToken ct)
    {
        var key = Hash(systemPrompt);
        if (_systemCache.TryGetValue(key, out var entry) && entry.Expiry > DateTimeOffset.UtcNow)
            return entry.Name;

        var ttlSeconds = config.GetValue("LLM:Gemini:CacheTtlSeconds", 300);
        var url        = $"{BaseUrl[..BaseUrl.LastIndexOf('/')]}/cachedContents?key={apiKey}";
        var body = new
        {
            model             = $"models/{Model}",
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            ttl               = $"{ttlSeconds}s"
        };

        var client   = httpClientFactory.CreateClient("Gemini");
        var response = await client.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct)
            ?? throw new InvalidOperationException("Gemini cachedContents returned an empty body.");

        var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrEmpty(name)) return null;

        _systemCache[key] = (name, DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, ttlSeconds - 30)));
        logger.LogDebug("[Gemini] Created context cache {Name} (ttl {Ttl}s).", name, ttlSeconds);
        return name;
    }

    /// <summary>Completes a request that references a cached system instruction (no inline system_instruction).</summary>
    private async Task<string> SendCachedAsync(string cachedName, string userPrompt, string apiKey, CancellationToken ct)
    {
        var body = new
        {
            cachedContent = cachedName,
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = userPrompt } } }
            },
            generationConfig = new
            {
                temperature      = 0.2,
                maxOutputTokens  = 32768,
                responseMimeType = "application/json",
                thinkingConfig   = new { thinkingBudget = 0 }
            }
        };

        return await SendCompleteAsync(Model, body, apiKey, ct);
    }

    /// <summary>
    /// Reports actual token usage (incl. implicitly-cached tokens) to the telemetry side-channel.
    /// Gemini's <c>promptTokenCount</c> already includes cached tokens; <c>cachedContentTokenCount</c>
    /// is the cached subset.
    /// </summary>
    private static void ReportUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var u)) return;

        int prompt = GetInt(u, "promptTokenCount");
        int output = GetInt(u, "candidatesTokenCount");
        int cached = GetInt(u, "cachedContentTokenCount");

        MeridianStudio.API.Infrastructure.Telemetry.LlmUsageScope.Current =
            new MeridianStudio.API.Infrastructure.Telemetry.LlmCallUsage(
                InputTokens: prompt,
                OutputTokens: output,
                CachedInputTokens: cached);
    }

    private static int GetInt(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;

    private static string Hash(string s)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes);
    }

    private async Task<string> SendCompleteAsync(string model, object body, string apiKey, CancellationToken ct)
    {
        var url    = $"{BaseUrl}/{model}:generateContent?key={apiKey}";
        var client = httpClientFactory.CreateClient("Gemini");

        logger.LogDebug("[Gemini] POST {Url}", $"{BaseUrl}/{model}:generateContent?key=***");

        var response = await client.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();   // surfaces 429/503 as HttpRequestException

        // Buffer the full response as a string before any parsing.
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Gemini returned an empty body.");

        // Extract the outermost JSON object by finding the first '{' and the last '}'.
        // This strips any prefix or suffix bytes injected by APM agents (e.g. Dynatrace
        // OneAgent) around the response, without touching the content in between.
        var jsonStart = raw.IndexOf('{');
        var jsonEnd   = raw.LastIndexOf('}');

        if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
        {
            logger.LogError("[Gemini] Response contains no JSON object. Raw body: {Body}", raw);
            throw new InvalidOperationException("Gemini response contained no JSON object.");
        }

        if (jsonStart > 0 || jsonEnd < raw.Length - 1)
            logger.LogDebug(
                "[Gemini] Stripped {Pre} prefix byte(s) and {Suf} suffix byte(s) from response.",
                jsonStart, raw.Length - jsonEnd - 1);

        var jsonStr = raw[jsonStart..(jsonEnd + 1)];

        // Progressive parse — mirrors LLMResponseParser.ParseDoc.
        JsonDocument? doc = null;
        JsonException? lastEx = null;

        try { doc = JsonDocument.Parse(jsonStr); }
        catch (JsonException jex) { lastEx = jex; }

        if (doc is null)
        {
            try { doc = JsonDocument.Parse(EscapeControlsInStrings(jsonStr)); }
            catch (JsonException jex) { lastEx = jex; }
        }

        if (doc is null)
        {
            var text = ExtractTextByBoundary(jsonStr);
            if (text is not null)
            {
                logger.LogWarning(
                    "[Gemini] Outer response JSON was malformed — fell back to boundary extraction. " +
                    "Parse error was: {Err}", lastEx?.Message);
                return text;
            }

            var failByte = (int)(lastEx?.BytePositionInLine ?? 0);
            var safe = Math.Min(jsonStr.Length, failByte + 80);
            var window = failByte > 80 ? jsonStr[(failByte - 80)..safe] : jsonStr[..safe];
            logger.LogError(
                lastEx,
                "[Gemini] All parse attempts failed — line {Line}, byte {Byte}. " +
                "Context: [{Window}]. Body size: {Len} bytes.",
                lastEx?.LineNumber, lastEx?.BytePositionInLine, window, raw.Length);
            throw new InvalidOperationException(
                $"Gemini returned unparseable response: {lastEx?.Message}", lastEx);
        }

        using (doc)
        {
            ReportUsage(doc.RootElement);

            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return text ?? throw new InvalidOperationException("Gemini returned null text.");
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt, string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = config["LLM:Gemini:ApiKey"]
            ?? throw new InvalidOperationException("LLM:Gemini:ApiKey is not set.");

        // Streaming is incompatible with responseMimeType — omit it here.
        var body = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = userPrompt } } }
            },
            generationConfig = new
            {
                temperature     = 0.2,
                maxOutputTokens = 32768,
                thinkingConfig  = new { thinkingBudget = 0 }
            }
        };

        // Serialize with the anonymous type's static type here — passing `body` as `object`
        // to a helper would resolve to JsonContent.Create<object>() which serializes `{}`.
        var bodyJson = System.Text.Json.JsonSerializer.Serialize(body);

        // OpenStreamAsync handles 503 / connection-drop / timeout retries with the lite
        // model before propagating the failure to the caller — same resilience as CompleteAsync.
        // StringContent is passed (not HttpContent) so it can be reused across retry attempts.
        var response = await OpenStreamAsync(Model, bodyJson, apiKey, ct);

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
        {
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data is "[DONE]") break;

            string? text = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var candidates = doc.RootElement.GetProperty("candidates");
                if (candidates.GetArrayLength() == 0) continue;
                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                if (parts.GetArrayLength() == 0) continue;

                // Skip thinking tokens — Gemini may emit these even when thinkingBudget=0.
                // They are marked with "thought": true and must not be mixed into the
                // assembled response text (they corrupt JSON extraction).
                var part = parts[0];
                if (part.TryGetProperty("thought", out var thought) && thought.GetBoolean())
                    continue;

                text = part.GetProperty("text").GetString();
            }
            catch { /* malformed chunk — skip */ }

            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    // ── Streaming helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Opens a streaming HTTP connection with automatic lite-model retry on 503,
    /// connection drop (ResponseEnded), or HttpClient timeout — mirrors the retry
    /// logic in <see cref="CompleteAsync"/> so streaming is equally resilient.
    /// </summary>
    private async Task<HttpResponseMessage> OpenStreamAsync(
        string model, string bodyJson, string apiKey, CancellationToken ct)
    {
        try
        {
            return await AttemptOpenStreamAsync(model, bodyJson, apiKey, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            logger.LogWarning("[Gemini/Stream] {Model} returned HTTP 503 — retrying with lite model {Lite}.", model, LiteModel);
            return await AttemptOpenStreamAsync(LiteModel, bodyJson, apiKey, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null && ex.InnerException is IOException)
        {
            logger.LogWarning(ex, "[Gemini/Stream] {Model} connection dropped — retrying with lite model {Lite}.", model, LiteModel);
            return await AttemptOpenStreamAsync(LiteModel, bodyJson, apiKey, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("[Gemini/Stream] {Model} timed out — retrying with lite model {Lite}.", model, LiteModel);
            return await AttemptOpenStreamAsync(LiteModel, bodyJson, apiKey, ct);
        }
    }

    private async Task<HttpResponseMessage> AttemptOpenStreamAsync(
        string model, string bodyJson, string apiKey, CancellationToken ct)
    {
        var url    = $"{BaseUrl}/{model}:streamGenerateContent?alt=sse&key={apiKey}";
        var client = httpClientFactory.CreateClient("Gemini");
        logger.LogDebug("[Gemini] Stream POST {Url}", $"{BaseUrl}/{model}:streamGenerateContent?alt=sse&key=***");

        // StringContent is reusable — safe to pass the same instance to retry attempts.
        var content = new System.Net.Http.StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        var response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return response;
    }

    // ── Parsing helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Escapes raw control characters (newline, carriage return, tab) that appear
    /// inside JSON string values without being escaped.  Leaves already-escaped
    /// sequences (\\n etc.) untouched.  Mirrors LLMResponseParser.EscapeControlsInStrings.
    /// </summary>
    private static string EscapeControlsInStrings(string json)
    {
        var sb     = new System.Text.StringBuilder(json.Length + 64);
        bool inStr = false, esc = false;
        foreach (var c in json)
        {
            if (esc)  { sb.Append(c); esc = false; continue; }
            if (c == '\\') { esc = true;  sb.Append(c); continue; }
            if (c == '"')  { inStr = !inStr; sb.Append(c); continue; }
            if (inStr)
                switch (c)
                {
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\t': sb.Append("\\t"); continue;
                }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Last-resort: extracts the value of the first <c>"text"</c> key by character
    /// position rather than JSON parsing, then unescapes standard JSON sequences.
    /// Works even when the outer response JSON is structurally broken.
    /// </summary>
    private static string? ExtractTextByBoundary(string json)
    {
        var keyIdx = json.IndexOf(@"""text""", StringComparison.Ordinal);
        if (keyIdx < 0) return null;

        var colonIdx = json.IndexOf(':', keyIdx + 6);
        if (colonIdx < 0) return null;

        var openQuote = json.IndexOf('"', colonIdx + 1);
        if (openQuote < 0) return null;

        var valueStart = openQuote + 1;

        // Walk backwards from the end to find the closing quote of the "text" value.
        var end = json.Length - 1;
        while (end > valueStart && json[end] != '"') end--;
        if (end <= valueStart) return null;

        return json[valueStart..end]
            .Replace("\\n",  "\n")
            .Replace("\\r",  "\r")
            .Replace("\\t",  "\t")
            .Replace("\\\"", "\"")
            .Trim();
    }
}
