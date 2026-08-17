# ADR-013 — Gemini HttpClient Uses SocketsHttpHandler

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

During development with Dynatrace OneAgent installed, Gemini API calls would intermittently fail with `JsonException` or return corrupted response streams. The root cause was that APM agents (Dynatrace OneAgent, New Relic, AppDynamics) hook into the default `HttpClientHandler` by replacing it in the handler pipeline. Gemini's REST API returns streaming JSON that must be read atomically; an APM agent intercepting and buffering the stream can truncate or corrupt the response body before `LLMResponseParser` sees it.

The standard `HttpClientHandler` is injectable by APM agents because it delegates to the platform's native HTTP stack through an extension point that agents routinely wrap. `SocketsHttpHandler` bypasses this extension point — it is a fully managed, self-contained .NET implementation of HTTP/1.1 and HTTP/2 with no injection surface.

Groq and Claude were not affected: Groq responses are smaller and arrive in a single buffer, and Claude's SDK handles its own streaming internally.

## Decision

The `"Gemini"` named `HttpClient` in `Program.cs` is configured with a custom primary message handler:

```csharp
builder.Services.AddHttpClient("Gemini", client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(15),
});
```

`SocketsHttpHandler` is used exclusively for Gemini because:
1. It has no APM injection surface — the response stream reaches `GeminiProvider` unmodified
2. It supports connection pooling via `PooledConnectionLifetime` — connections are recycled every 15 minutes to avoid DNS staleness on long-running processes
3. It supports HTTP/2 natively — future Gemini API versions that use HTTP/2 streaming will work without a handler change

`PooledConnectionLifetime = 15 minutes` follows the [Microsoft guidance](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines) for `IHttpClientFactory`-managed clients to prevent DNS caching issues.

Groq and Claude continue to use the default `HttpClientHandler` — both are unaffected by APM interception and benefit from the agent's telemetry.

## Consequences

### Positive
- Gemini calls are stable regardless of which APM agent is installed in the runtime environment
- `SocketsHttpHandler` has lower overhead than the default handler on Linux (no P/Invoke to curl) — marginal latency improvement on non-Windows deployments
- Connection pooling with explicit lifetime prevents the "DNS poisoning on long-running containers" failure mode

### Negative / Trade-offs
- `SocketsHttpHandler` does not support HTTP/1.0 or certain proxy configurations that the default handler does — not a concern for a direct API call to `generativelanguage.googleapis.com`, but would matter if the deployment adds an intercepting proxy
- The handler divergence between providers (SocketsHttpHandler for Gemini, default for others) adds a small cognitive overhead when debugging connection issues — the handler type must be considered

### Failure Modes
- If Gemini's API adds certificate pinning or a custom TLS policy in the future, the SocketsHttpHandler configuration would need to be updated — the default handler would pick up OS trust store changes automatically, while SocketsHttpHandler requires explicit `SslOptions` configuration for custom TLS behaviour
- `PooledConnectionLifetime = 15m` means a Gemini API key rotation does not take effect until existing pooled connections expire — a server restart is required to pick up a new key immediately
