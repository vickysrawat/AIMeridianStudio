# ADR-014 — DI Lifetime Strategy: Singleton Infrastructure, Scoped Application

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

ASP.NET Core DI offers three service lifetimes: Singleton (one instance for the process lifetime), Scoped (one instance per HTTP request), and Transient (new instance per injection). Choosing the wrong lifetime is a common source of bugs:

- A Singleton that holds a `DbContext` or an `HttpContext` reference causes data leaks between requests
- A Scoped service injected into a Singleton triggers the "captive dependency" anti-pattern — the Singleton holds the Scoped instance beyond its intended lifetime
- Transient services that hold locks or open connections cause resource leaks

MeridianStudio has two distinct categories of services:
1. **Infrastructure services** — hold shared mutable state (caches, broadcaster channel, provider runtime status, LLM provider pool, disk-based training stores)
2. **Application services** — orchestrate a single request, using injected infrastructure

## Decision

**Infrastructure services are registered as Singleton:**

```csharp
builder.Services.AddSingleton<ModelStatusBroadcaster>();  // SSE channel
builder.Services.AddSingleton<PayloadCache>();            // in-memory + disk cache
builder.Services.AddSingleton<LLMOrchestrator>();         // provider chain + runtime state
builder.Services.AddSingleton<LocalCompilationEngine>();  // stateless heuristic engine
builder.Services.AddSingleton<SelectionBankService>();    // disk-based selection store
builder.Services.AddSingleton<DocumentBankService>();     // disk-based quality store
builder.Services.AddSingleton<ILLMProvider, GeminiProvider>();
builder.Services.AddSingleton<ILLMProvider, GroqProvider>();
builder.Services.AddSingleton<ILLMProvider, ClaudeProvider>();
```

Rationale: the in-memory cache (`ConcurrentDictionary` in `PayloadCache`), the SSE broadcaster's connected-clients channel, and the LLM provider's runtime state dictionary (`_runtimeStates` in `LLMOrchestrator`) must survive across requests to be useful. Making them Scoped would create a new instance per request, destroying the cache on every call.

`LLMProvider` implementations are Singleton because they hold a configured `HttpClient` reference (obtained from `IHttpClientFactory`) — sharing the client across requests is the correct pattern for `HttpClient`.

**Application services are registered as Scoped:**

```csharp
builder.Services.AddScoped<IResearchService,          ResearchService>();
builder.Services.AddScoped<IBlueprintService,         BlueprintService>();
builder.Services.AddScoped<ITaskExecutionService,     TaskExecutionService>();
builder.Services.AddScoped<IDocumentService,          DocumentService>();
builder.Services.AddScoped<IPromptService,            PromptService>();
builder.Services.AddScoped<IDomainService,            DomainService>();
builder.Services.AddScoped<IMissionSuggestionService, MissionSuggestionService>();
builder.Services.AddScoped<DocumentGoalJudgeService>();
builder.Services.AddScoped<SolutionClassifierService>();
```

Rationale: application services are stateless orchestrators — they inject infrastructure Singletons plus the request-scoped `IConfiguration` and `ILogger<T>`. Scoped is correct here because:
- It matches the request boundary semantically (one service instance per API call)
- It avoids any risk of cross-request state contamination
- Scoped services are automatically disposed at the end of the request — any `IDisposable` resources are cleaned up correctly

**No Transient services** are registered — they would be wasteful for services with non-trivial construction cost (logger, configuration) and unnecessary because all services are either stateful-Singleton or stateless-Scoped.

## Consequences

### Positive
- Cache, broadcaster, and provider state persist correctly across requests — the application behaves correctly without any special session management
- Captive dependency risk is eliminated: Scoped services inject Singletons (safe), never the reverse
- The Singleton `LLMOrchestrator` accumulates real runtime state (`_runtimeStates`) across all requests, enabling the `/api/provider-status` endpoint to return accurate current status without a separate store

### Negative / Trade-offs
- Singleton services must be thread-safe — all shared mutable state uses `ConcurrentDictionary`, `SemaphoreSlim`, or immutable patterns. A new developer adding a `List<T>` field to a Singleton will introduce a race condition.
- Singleton `LLMProvider` instances hold `HttpClient` — if the provider's base URL changes (e.g., API endpoint migration), a process restart is required; there is no hot-reload path

### Failure Modes
- ASP.NET Core's built-in Singleton validation (enabled in development: `ValidateScopes = true`) will throw on startup if a Scoped service is injected into a Singleton — this is the safety net, but it only runs in development mode. The production build will silently accept the captive dependency and exhibit data corruption under load.
- `SelectionBankService` and `DocumentBankService` use `SemaphoreSlim` for disk write serialisation — they are safe as Singletons, but the semaphore is process-local. If the application is ever scaled to multiple instances, concurrent writes to the same disk file from different processes will corrupt the JSON.
