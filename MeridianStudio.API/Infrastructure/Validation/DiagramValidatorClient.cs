using System.Net.Http.Json;
using System.Text.Json;

namespace MeridianStudio.API.Infrastructure.Validation;

/// <summary>
/// Typed HttpClient over the Node validator sidecar. Reads <c>Validator:*</c> config; all calls are
/// wrapped so any failure/timeout returns null (fail-soft). Register the named HttpClient "Validator"
/// with the configured base URL + timeout in Program.cs.
/// </summary>
public sealed class DiagramValidatorClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<DiagramValidatorClient> logger) : IDiagramValidator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private int _loggedDisabled;

    public bool Enabled => config.GetValue("Validator:Enabled", false);

    public Task<DiagramValidationResult?> ValidateAsync(string source, CancellationToken ct = default)
        => PostAsync<DiagramValidationResult>("/validate/diagram", new { source }, ct);

    public Task<DiagramRepairResult?> RepairAsync(string source, CancellationToken ct = default)
        => PostAsync<DiagramRepairResult>("/repair/diagram", new { source }, ct);

    public Task<DocumentValidationResult?> ValidateDocumentAsync(string markdown, CancellationToken ct = default)
        => PostAsync<DocumentValidationResult>("/validate/document", new { markdown }, ct);

    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct) where T : class
    {
        if (!Enabled)
        {
            if (Interlocked.Exchange(ref _loggedDisabled, 1) == 0)
                logger.LogInformation("[Validator] Disabled (Validator:Enabled=false) — content passes through unchanged.");
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient("Validator");
            using var resp = await client.PostAsJsonAsync(path, body, Json, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("[Validator] {Path} returned {Status} — passing content through.", path, (int)resp.StatusCode);
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (Exception ex)
        {
            // Fail-soft: never let a validator hiccup affect generation.
            logger.LogWarning(ex, "[Validator] {Path} failed — passing content through unchanged.", path);
            return null;
        }
    }
}
