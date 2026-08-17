using System.Net.Http.Json;
using System.Text.Json;

namespace MeridianStudio.API.Infrastructure.LLM.Embedding;

/// <summary>
/// Embeddings via Gemini's text-embedding endpoint, reusing the named "Gemini" HttpClient and
/// the LLM:Gemini:ApiKey secret. Single calls use :embedContent; batches use :batchEmbedContents.
/// Throws on failure so callers can fall back to the lexical provider or legacy ranking.
/// </summary>
public sealed class GeminiEmbeddingProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<GeminiEmbeddingProvider> logger) : IEmbeddingProvider
{
    // text-embedding-004 / embedding-001 now return 404 on the v1beta endpoint for
    // current API keys — gemini-embedding-001 is the supported replacement.
    private const string DefaultModel = "gemini-embedding-001";
    private const string BaseUrl      = "https://generativelanguage.googleapis.com/v1beta/models";

    private string Model => config["LLM:Gemini:EmbeddingModel"] ?? DefaultModel;

    public string SpaceId => $"gemini:{Model}";

    public bool IsRealModel => !string.IsNullOrWhiteSpace(config["LLM:Gemini:ApiKey"]);

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var apiKey = ApiKey();
        var url    = $"{BaseUrl}/{Model}:embedContent?key={apiKey}";
        var body   = new
        {
            model   = $"models/{Model}",
            content = new { parts = new[] { new { text } } }
        };

        var client   = httpClientFactory.CreateClient("Gemini");
        var response = await client.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct)
            ?? throw new InvalidOperationException("Gemini embedding returned an empty body.");

        return ReadValues(doc.RootElement.GetProperty("embedding"));
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        var apiKey = ApiKey();
        var url    = $"{BaseUrl}/{Model}:batchEmbedContents?key={apiKey}";
        var body   = new
        {
            requests = texts.Select(t => new
            {
                model   = $"models/{Model}",
                content = new { parts = new[] { new { text = t } } }
            }).ToArray()
        };

        var client   = httpClientFactory.CreateClient("Gemini");
        var response = await client.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct)
            ?? throw new InvalidOperationException("Gemini batch embedding returned an empty body.");

        var result = new List<float[]>(texts.Count);
        foreach (var item in doc.RootElement.GetProperty("embeddings").EnumerateArray())
            result.Add(ReadValues(item));

        logger.LogDebug("[Gemini/Embed] Embedded {N} text(s) with {Model}.", texts.Count, Model);
        return result;
    }

    private string ApiKey() =>
        config["LLM:Gemini:ApiKey"]
        ?? throw new InvalidOperationException("LLM:Gemini:ApiKey is not set.");

    private static float[] ReadValues(JsonElement embedding)
    {
        var values = embedding.GetProperty("values");
        var arr    = new float[values.GetArrayLength()];
        var i      = 0;
        foreach (var v in values.EnumerateArray())
            arr[i++] = v.GetSingle();
        return arr;
    }
}
