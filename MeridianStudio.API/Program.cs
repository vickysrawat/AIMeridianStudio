using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;
using MeridianStudio.API.API.Endpoints;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Application.Services;
using MeridianStudio.API.Application.Services.Persistence;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.ExampleBank;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.LLM.Providers;
using MeridianStudio.API.Infrastructure.LLM.Embedding;
using MeridianStudio.API.Infrastructure.LocalEngine;
using MeridianStudio.API.Infrastructure.Realtime;
using MeridianStudio.API.Infrastructure.Persistence;
using MeridianStudio.API.Infrastructure.Security;
using MeridianStudio.API.Infrastructure.Telemetry;
using MeridianStudio.API.Infrastructure.Tokenization;
using Scalar.AspNetCore;

// QuestPDF Community licence (free for orgs under the revenue threshold / internal tooling).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ── JSON serialization ────────────────────────────────────────────────────────
builder.Services.ConfigureHttpJsonOptions(options =>
{
    var json = options.SerializerOptions;
    json.PropertyNamingPolicy        = JsonNamingPolicy.CamelCase;
    json.PropertyNameCaseInsensitive = true;   // ensures camelCase JSON binds to PascalCase record constructor params
    json.DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull;
    json.WriteIndented               = false;
    json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// ── CORS ──────────────────────────────────────────────────────────────────────
const string CorsPolicyName = "MeridianCorsPolicy";

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        var origins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>();

        if (origins is null or ["*"])
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        else
            policy.WithOrigins(origins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
    });
});

// ── OpenAPI ───────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── Named HttpClients for LLM providers ──────────────────────────────────────
// Gemini uses SocketsHttpHandler directly so APM agents (e.g. Dynatrace OneAgent)
// that wrap HttpClientHandler cannot intercept or modify the response stream.
builder.Services.AddHttpClient("Gemini", client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(15),
})
// Transient-fault retry (503/429/5xx/timeout/network). All Gemini surfaces — chat,
// embeddings, and live Google-Search grounding — share this client, so a momentary
// Google-side blip self-heals before any provider-level fallback is triggered.
// NOTE: deliberately NOT AddStandardResilienceHandler — its 10s/30s default timeouts
// would fight the intentional 90s client timeout above.
.AddResilienceHandler("gemini-retry", b => b.AddRetry(new HttpRetryStrategyOptions
{
    MaxRetryAttempts = 3,
    BackoffType      = DelayBackoffType.Exponential,
    UseJitter        = true,
    Delay            = TimeSpan.FromMilliseconds(500),
    // Retry the usual transient faults (5xx, 408, 429, network blips, timeouts) BUT fail fast on a
    // DNS-resolution failure (SocketError.HostNotFound) — the host won't start resolving inside the
    // backoff window, so retrying only delays the heuristic fallback when the box is offline.
    ShouldHandle = args =>
    {
        var ex = args.Outcome.Exception;
        if (IsHostNotFound(ex)) return ValueTask.FromResult(false);
        var transient =
            ex is HttpRequestException
            || ex is Polly.Timeout.TimeoutRejectedException
            || (args.Outcome.Result is { } resp
                && ((int)resp.StatusCode >= 500
                    || resp.StatusCode == System.Net.HttpStatusCode.RequestTimeout
                    || resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests));
        return ValueTask.FromResult(transient);
    },
}));

// Walks the inner-exception chain for an unresolvable-host socket error (offline/DNS down).
static bool IsHostNotFound(Exception? ex)
{
    for (var e = ex; e is not null; e = e.InnerException)
        if (e is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.HostNotFound })
            return true;
    return false;
}

builder.Services.AddHttpClient("Groq", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddHttpClient("Claude", client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
});

// Browserless Mermaid validator sidecar — short timeout so it can never add more than TimeoutMs
// to the response path (fail-soft on timeout). Base URL + timeout are config-driven.
builder.Services.AddHttpClient("Validator", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Validator:BaseUrl"] ?? "http://localhost:5177");
    client.Timeout = TimeSpan.FromMilliseconds(builder.Configuration.GetValue("Validator:TimeoutMs", 500));
});

// ── LLM Providers — registered in PRIORITY ORDER ─────────────────────────────
// Orchestrator tries them left-to-right: Gemini → Groq → Claude → Heuristic.
// Providers with an empty ApiKey are skipped automatically.
builder.Services.AddSingleton<ILLMProvider, GeminiProvider>(); // 1st priority
builder.Services.AddSingleton<ILLMProvider, GroqProvider>();   // 2nd priority
builder.Services.AddSingleton<ILLMProvider, ClaudeProvider>(); // 3rd priority

// ── Infrastructure ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<ModelStatusBroadcaster>();

// ── Web Search providers (live trend enrichment) ──────────────────────────────
builder.Services.AddSingleton<MeridianStudio.API.Infrastructure.WebSearch.TavilySearchProvider>();
builder.Services.AddSingleton<MeridianStudio.API.Infrastructure.WebSearch.SerperSearchProvider>();
builder.Services.AddSingleton<MeridianStudio.API.Infrastructure.WebSearch.PubMedSearchProvider>();
builder.Services.AddSingleton<MeridianStudio.API.Infrastructure.WebSearch.GitHubTrendingProvider>();
builder.Services.AddSingleton<MeridianStudio.API.Infrastructure.LLM.GeminiGroundingProvider>();
builder.Services.AddSingleton<MeridianStudio.API.Infrastructure.WebSearch.WebResearchEnricher>();
builder.Services.AddHttpClient("Tavily");
builder.Services.AddHttpClient("Serper");
builder.Services.AddHttpClient("GitHub");
builder.Services.AddSingleton<PayloadCache>();
builder.Services.AddSingleton<SemanticCache>();

// ── Tokenization + cost/token telemetry (measurement foundation) ────
builder.Services.AddSingleton<ITokenCounter, TokenCounter>();
builder.Services.AddSingleton<LlmTelemetry>();
if (builder.Configuration.GetValue("Telemetry:Persist", false))
    builder.Services.AddSingleton<ILlmTelemetry>(sp => new PersistentLlmTelemetry(
        sp.GetRequiredService<LlmTelemetry>(),
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<ILogger<PersistentLlmTelemetry>>()));
else
    builder.Services.AddSingleton<ILlmTelemetry>(sp => sp.GetRequiredService<LlmTelemetry>());

builder.Services.AddSingleton<LLMOrchestrator>();
builder.Services.AddSingleton<LocalCompilationEngine>();

// ── Embeddings (semantic retrieval) — Gemini when keyed, else offline lexical ──
// Bounded in-memory cache backs the embedding decorator (entries use Size = 1).
builder.Services.AddMemoryCache(o => o.SizeLimit = 10_000);
builder.Services.AddSingleton<GeminiEmbeddingProvider>();
builder.Services.AddSingleton<LexicalEmbeddingProvider>();
builder.Services.AddSingleton<IEmbeddingProvider>(sp =>
{
    var gemini = sp.GetRequiredService<GeminiEmbeddingProvider>();
    IEmbeddingProvider chosen = gemini.IsRealModel
        ? gemini
        : sp.GetRequiredService<LexicalEmbeddingProvider>();
    // Cache query/candidate embeddings so repeated retrievals skip the network call
    // and reduce exposure to transient Gemini failures.
    return new CachingEmbeddingProvider(chosen, sp.GetRequiredService<IMemoryCache>());
});
builder.Services.AddSingleton<IDomainClassifier, DomainClassifier>();

// ── Example Bank (disk-based training stores) ─────────────────────────────
builder.Services.AddSingleton<SelectionBankService>();
builder.Services.AddSingleton<DocumentBankService>();

// ── Artifact persistence (durable knowledge store) ─────────────────────────
// Disk store is the working default (zero dependencies, clean NuGet audit); the SQLite EF
// store drops in behind IArtifactStore once SQLitePCLRaw ships SQLite ≥ 3.50.2 (CVE-2025-6965).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<MeridianStudio.API.Infrastructure.Security.ITenantAccessor,
                           MeridianStudio.API.Infrastructure.Security.TenantAccessor>();
builder.Services.AddSingleton<IArtifactStore,
                              MeridianStudio.API.Infrastructure.Persistence.DiskArtifactStore>();

// Configurable auth (OFF in dev via Auth:Enabled) — see AuthExtensions.
builder.Services.AddMeridianAuth(builder.Configuration);

// Retention purge only when a positive RetentionDays is configured.
if (builder.Configuration.GetValue("Persistence:RetentionDays", 0) > 0)
    builder.Services.AddHostedService<RetentionService>();

// ── Application Services (wrapped with persistence decorators) ───────────────
// Concrete services register plainly; the public interface resolves to the decorator, which
// persists each result then delegates. Inner services stay byte-for-byte unchanged.
builder.Services.AddScoped<ResearchService>();
builder.Services.AddScoped<IResearchService>(sp => new PersistingResearchService(
    sp.GetRequiredService<ResearchService>(), sp.GetRequiredService<IArtifactStore>(),
    sp.GetRequiredService<PayloadCache>(), sp.GetRequiredService<MeridianStudio.API.Infrastructure.Security.ITenantAccessor>(),
    sp.GetRequiredService<ILogger<PersistingResearchService>>()));

builder.Services.AddScoped<OpportunityGroundingResolver>();
builder.Services.AddScoped<BlueprintService>();
builder.Services.AddScoped<IBlueprintService>(sp => new PersistingBlueprintService(
    sp.GetRequiredService<BlueprintService>(), sp.GetRequiredService<IArtifactStore>(),
    sp.GetRequiredService<PayloadCache>(), sp.GetRequiredService<MeridianStudio.API.Infrastructure.Security.ITenantAccessor>(),
    sp.GetRequiredService<ILogger<PersistingBlueprintService>>()));
builder.Services.AddScoped<IBlueprintReadinessService, BlueprintReadinessService>();

builder.Services.AddScoped<IBlueprintChatService,    BlueprintChatService>();
builder.Services.AddScoped<AssessmentService>();
builder.Services.AddScoped<IAssessmentService>(sp => new PersistingAssessmentService(
    sp.GetRequiredService<AssessmentService>(), sp.GetRequiredService<IArtifactStore>(),
    sp.GetRequiredService<PayloadCache>(), sp.GetRequiredService<MeridianStudio.API.Infrastructure.Security.ITenantAccessor>(),
    sp.GetRequiredService<ILogger<PersistingAssessmentService>>()));

builder.Services.AddScoped<TaskExecutionService>();
builder.Services.AddScoped<ITaskExecutionService>(sp => new PersistingTaskExecutionService(
    sp.GetRequiredService<TaskExecutionService>(), sp.GetRequiredService<IArtifactStore>(),
    sp.GetRequiredService<PayloadCache>(), sp.GetRequiredService<MeridianStudio.API.Infrastructure.Security.ITenantAccessor>(),
    sp.GetRequiredService<ILogger<PersistingTaskExecutionService>>()));

builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<IDocumentService>(sp => new PersistingDocumentService(
    sp.GetRequiredService<DocumentService>(), sp.GetRequiredService<IArtifactStore>(),
    sp.GetRequiredService<PayloadCache>(), sp.GetRequiredService<MeridianStudio.API.Infrastructure.Security.ITenantAccessor>(),
    sp.GetRequiredService<ILogger<PersistingDocumentService>>()));

builder.Services.AddScoped<PromptService>();
builder.Services.AddScoped<IPromptService>(sp => new PersistingPromptService(
    sp.GetRequiredService<PromptService>(), sp.GetRequiredService<IArtifactStore>(),
    sp.GetRequiredService<PayloadCache>(), sp.GetRequiredService<MeridianStudio.API.Infrastructure.Security.ITenantAccessor>(),
    sp.GetRequiredService<ILogger<PersistingPromptService>>()));

builder.Services.AddScoped<IDomainService,           DomainService>();
builder.Services.AddScoped<IMissionSuggestionService, MissionSuggestionService>();
builder.Services.AddScoped<IUseCaseAnalysisService,  UseCaseAnalysisService>();
builder.Services.AddScoped<IDocumentReviewService,   DocumentReviewService>();
builder.Services.AddScoped<DocumentGoalJudgeService>();
builder.Services.AddScoped<SolutionClassifierService>();

// ── Analysis over persisted artifacts (comparison + cross-run analytics) ────
builder.Services.AddScoped<ComparisonService>();
builder.Services.AddScoped<CrossRunAnalyticsService>();
builder.Services.AddScoped<WhitePaperService>();

// ── Browserless diagram/document validation (gated by Validator:Enabled) ────
builder.Services.AddSingleton<MeridianStudio.API.Infrastructure.Validation.IDiagramValidator,
                              MeridianStudio.API.Infrastructure.Validation.DiagramValidatorClient>();
builder.Services.AddSingleton<MeridianStudio.API.Infrastructure.Diagnostics.ILearnedMermaidFixStore,
                              MeridianStudio.API.Infrastructure.Diagnostics.LearnedMermaidFixStore>();
builder.Services.AddScoped<DocumentValidationService>();
builder.Services.AddSingleton<MeridianStudio.API.Infrastructure.Diagnostics.SelfCheckService>();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Log which providers are active at startup
using (var scope = app.Services.CreateScope())
{
    var providers = scope.ServiceProvider.GetServices<ILLMProvider>().ToList();
    var active    = providers.Where(p => p.IsConfigured).Select(p => p.Name).ToList();
    var inactive  = providers.Where(p => !p.IsConfigured).Select(p => p.Name).ToList();

    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    if (active.Count > 0)
        logger.LogInformation("[LLM] Active providers (priority order): {Providers}", string.Join(" → ", active));
    if (inactive.Count > 0)
        logger.LogInformation("[LLM] Inactive (no API key): {Providers}", string.Join(", ", inactive));
    if (active.Count == 0)
        logger.LogWarning("[LLM] No providers configured — all requests will use the heuristic engine.");

    var embedder = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>();
    logger.LogInformation("[Embeddings] Active space: {Space} ({Kind}).",
        embedder.SpaceId, embedder.IsRealModel ? "hosted model" : "offline lexical fallback");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title       = "MeridianStudio API";
        options.Theme       = ScalarTheme.DeepSpace;
        options.DefaultHttpClient = new(ScalarTarget.Http, ScalarClient.HttpClient);
    });
}

app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);

// Auth middleware only runs when Auth:Enabled (a registered scheme exists).
var authEnabled = app.Configuration.IsAuthEnabled();
if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// ── Route groups ──────────────────────────────────────────────────────────────
var api = app.MapGroup("/api");
if (authEnabled)
    api.RequireAuthorization();   // whole API requires a valid token in prod

api.MapGet("/health", () => Results.Ok(new
    {
        status  = "healthy",
        service = "MeridianStudio.API",
        utc     = DateTimeOffset.UtcNow
    }))
   .WithName("HealthCheck")
   .WithTags("Health")
   .AllowAnonymous()   // health + SSE stay reachable without a token
   .Produces<object>(StatusCodes.Status200OK);

api.MapResearchEndpoints();
api.MapBlueprintEndpoints();
api.MapAssessmentEndpoints();
api.MapTaskEndpoints();
api.MapDocumentEndpoints();
api.MapPromptEndpoints();
api.MapDomainEndpoints();
api.MapProjectEndpoints();
api.MapModelStatusEndpoints();
api.MapProviderStatusEndpoints();
api.MapMissionEndpoints();
api.MapTelemetryEndpoints();
api.MapDiagnosticsEndpoints();
api.MapArtifactEndpoints();
api.MapComparisonEndpoints();
api.MapAnalyticsEndpoints();
api.MapWhitePaperEndpoints();
api.MapExportEndpoints();

app.Run();
