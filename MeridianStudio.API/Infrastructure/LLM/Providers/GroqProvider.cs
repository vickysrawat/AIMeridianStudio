using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MeridianStudio.API.Infrastructure.LLM.Providers;

/// <summary>
/// Groq provider using LLaMA 3.3 70B Versatile via the OpenAI-compatible API.
/// Uses <c>response_format: { type: "json_object" }</c> to enforce JSON output.
/// Named HttpClient: "Groq".
/// </summary>
public sealed class GroqProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<GroqProvider> logger) : ILLMProvider
{
    private const string DefaultModel = "llama-3.3-70b-versatile";
    private const string Endpoint     = "https://api.groq.com/openai/v1/chat/completions";

    private string Model => config["LLM:Groq:Model"] ?? DefaultModel;

    public string Name => $"Groq ({Model})";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(config["LLM:Groq:ApiKey"]);

    public async Task<string> CompleteAsync(
        string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var apiKey = config["LLM:Groq:ApiKey"]
            ?? throw new InvalidOperationException("LLM:Groq:ApiKey is not set.");

        var body = new
        {
            model    = Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   }
            },
            temperature     = 0.2,
            max_tokens      = 8192,
            response_format = new { type = "json_object" }  // OpenAI-compatible JSON mode
        };

        var client = httpClientFactory.CreateClient("Groq");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(body);

        logger.LogDebug("[Groq] POST {Endpoint} model={Model}", Endpoint, Model);

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();   // surfaces 429/503 as HttpRequestException

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct)
            ?? throw new InvalidOperationException("Groq returned empty body.");

        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return text ?? throw new InvalidOperationException("Groq returned null content.");
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt, string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = config["LLM:Groq:ApiKey"]
            ?? throw new InvalidOperationException("LLM:Groq:ApiKey is not set.");

        var body = new
        {
            model    = Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   }
            },
            temperature = 0.2,
            max_tokens  = 8192,
            stream      = true
        };

        var client = httpClientFactory.CreateClient("Groq");

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = JsonContent.Create(body);

        logger.LogDebug("[Groq] Stream POST {Endpoint} model={Model}", Endpoint, Model);

        var response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

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
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() == 0) continue;
                var delta = choices[0].GetProperty("delta");
                if (!delta.TryGetProperty("content", out var contentEl)) continue;
                text = contentEl.GetString();
            }
            catch { /* malformed chunk — skip */ }

            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }
}
