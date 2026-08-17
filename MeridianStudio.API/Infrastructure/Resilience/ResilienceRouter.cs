using System.Net;

namespace MeridianStudio.API.Infrastructure.Resilience;

/// <summary>
/// Wraps every external LLM call. On HTTP 429 / 503 it logs the divert message
/// and falls back to the local compilation engine. All other non-cancellation
/// exceptions are treated identically so the API never surfaces a 500.
/// Register as Singleton.
/// </summary>
public sealed class ResilienceRouter(ILogger<ResilienceRouter> logger)
{
    private const string QuotaMessage =
        "[Resilience Router] External API quota limit reached. " +
        "Route safely diverted to localized compilation engine.";

    public async Task<T> ExecuteAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T>> externalCall,
        Func<T> localFallback,
        CancellationToken ct = default)
    {
        try
        {
            return await externalCall(ct);
        }
        catch (HttpRequestException ex) when (IsQuotaOrUnavailable(ex.StatusCode))
        {
            logger.LogWarning(QuotaMessage);
            return localFallback();
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // External API timed out, not user-cancelled
            logger.LogWarning(
                "[Resilience Router] External API timed out in {Op}. Diverting to local engine.",
                operationName);
            return localFallback();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "[Resilience Router] Unexpected failure in {Op}. Diverting to local engine.",
                operationName);
            return localFallback();
        }
    }

    private static bool IsQuotaOrUnavailable(HttpStatusCode? code)
        => code is HttpStatusCode.TooManyRequests   // 429 RESOURCE_EXHAUSTED
                or HttpStatusCode.ServiceUnavailable; // 503 UNAVAILABLE
}

/// <summary>Thrown by a service when no external API key is configured.</summary>
public sealed class ExternalApiNotConfiguredException(string serviceName)
    : InvalidOperationException(
        $"External API client '{serviceName}' is not configured — local engine active.");
