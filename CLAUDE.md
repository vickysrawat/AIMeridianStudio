# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

MeridianStudio — an AI Solution Agent & System Architect Hub. From a domain/opportunity it produces prioritized research, system blueprints, corporate documents, developer-handoff prompts, white papers, and simulated execution — offline-capable via a heuristic fallback.

## Repository layout

```
AIMeridianStudio/
├── MeridianStudio.API/        .NET 10 Minimal API (C# 13, DDD). Solution: MeridianStudio.slnx
├── MeridianStudio.UI/         Angular 19 (standalone components + signals), Tailwind v4
├── MeridianStudio.Validator/  Node/Fastify browserless Mermaid validate+repair sidecar (gated OFF)
├── tests/MeridianStudio.Eval/ Console eval harness (golden-set briefs) — NOT a unit-test project
└── docs/ADR/                  30+ Architecture Decision Records — the "why" behind the design
```

## Commands

**API** (from `MeridianStudio.API/`)
- Run: `dotnet run --project MeridianStudio.API.csproj --launch-profile http` → `http://localhost:5000` (Scalar UI `/scalar/v1`, OpenAPI `/openapi/v1.json`).
- Build: `dotnet build MeridianStudio.API.csproj`. **The build is the lint gate**: `TreatWarningsAsErrors=true` + NuGet audit are on, so warnings/vulnerable packages fail the build.
- LLM keys (optional; app runs offline without them): `dotnet user-secrets set "LLM:Gemini:ApiKey" "…"` (also `LLM:Groq:ApiKey`, `LLM:Claude:ApiKey`).
- Eval harness (not unit tests): `dotnet run --project ../tests/MeridianStudio.Eval` — runs the golden-set briefs in `briefs.json` through generation.
- *Sandbox note:* if a normal build fails on a locked apphost .exe, build with `dotnet build MeridianStudio.API.csproj -p:UseAppHost=false -o obj/vf`.

**UI** (from `MeridianStudio.UI/`)
- Install then run: `npm install` → `npm start` → `http://localhost:4200` (expects the API on :5000; base URL injected via the `API_BASE_URL` token).
- Build: `npm run build`. Test: `npm test` (`ng test`). Single test: focus a spec with `fdescribe`/`fit`, or `npx ng test --include='**/foo.spec.ts'`.
- Tailwind is compiled **out-of-band** by the standalone CLI (ADR-016), not the PostCSS plugin: `npm run tw:build` (or `tw:watch`) rebuilds `src/styles.css` from `src/styles.base.css`.
- **Build hangs with no output?** A killed/orphaned build leaves the Angular incremental cache locked — `rm -rf .angular/cache` then reports `.angular/cache/**/angular-compiler.db-lock: Device or resource busy`, and every subsequent `ng build`/`tsc` hangs waiting on it. Fix: kill the stray build's Node process (target it by PID — do **not** blanket-kill `node.exe`, that can kill the Claude Code CLI), then delete `.angular/cache`; a terminal/OS restart also releases the lock. (OneDrive-synced working dirs make this more frequent.)
- **Lucide icons must be pre-registered** in `app.config.ts`'s `LucideAngularModule.pick({...})`. An unregistered `<lucide-icon name="…">` **throws at render** (not at build), so it passes `ng build` but errors live — add any new icon to that provider first.

**Validator sidecar** (from `MeridianStudio.Validator/`) — only needed to enable AI Mermaid self-healing
- Run: `npm install` → `npm start` → `http://localhost:5177`. Tests: `npm test` (vitest). Single: `npx vitest run src/mermaid-fixes.spec.ts` or `npx vitest run -t "quotes an edge label"`.
- Disabled by default; set `Validator:Enabled=true` (+ optional per-request opt-in) in the API to use it.

## High-Level Architecture

### Request pipeline (the spine)
Every generation follows the same shape — read these together to understand any feature:
`API/Endpoints/*` (Minimal-API handlers, `InputGuard` validation) → an `Application/Services/*` service → **`LLMOrchestrator.ExecuteAsync(operation, providerFn, heuristicFallback, ct)`** → `PromptBuilder` builds the prompt / `LLMResponseParser` parses it → result cached in `PayloadCache` and persisted by a **`Persisting*` decorator**. Services depend on interfaces in `Application/Interfaces`; DI + config live in `Program.cs`.

### Multi-model cascade + provenance
`LLMOrchestrator` tries providers in order **Gemini 2.5 Flash → Groq llama-3.3-70b → Claude Sonnet**, then a deterministic **`LocalCompilationEngine` (Heuristic Engine, offline)** as fail-soft fallback. Providers implement `ILLMProvider` (`IsConfigured`, `CompleteAsync`, `StreamAsync`). Every response carries a `modelUsed` string. The orchestrator `Emit`s routing events (`attempting`/`succeeded`/`failed`/`fallback`) to `ModelStatusBroadcaster`, streamed over SSE at **`GET /api/events/model-status`** (SSE over WebSocket — ADR-003). **Gemini forces `responseMimeType: application/json`, so prompts must return exact JSON** — typically `{"content": "<markdown>"}` or the documented record shape (ADR-006). The whole stack is **fail-soft**: a provider/store/sidecar failure degrades, it never breaks generation.

### Persistence, lineage & caching (the knowledge hub)
- **`IArtifactStore` / `DiskArtifactStore`** — disk-backed, **tenant-scoped**, append-only **versioned** store; `SaveAsync` **dedups on `(LineageId, RequestHash)`** and mints version N+1 otherwise. `ArtifactKind` = Research | Blueprint | TaskSpec | Document | DeveloperPrompt | Assessment. (SQLite deferred — see the memory note / ADR context.)
- **`ArtifactProjection.For*`** maps each result → `ArtifactMetadata` (stable `LineageId` slug, fine-grained `RequestHash`, cross-ref `Tags` like `blueprint:{id}`, and `ParentArtifactId` for the opportunity→blueprint→document→task chain).
- **`Persisting*Service` decorators** (`Application/Services/Persistence`) wrap the concrete services and save best-effort via `PersistenceGuard.SafeSaveAsync` (swallows storage errors). Streaming endpoints persist at the endpoint, not the decorator.
- **`PayloadCache`** — two-layer memory+disk cache keyed by `ComputeKey` (SHA-256 of the payload), survives restarts (ADR-007). Blueprints are additionally cached under `bp-by-id:{id}`, assessments under `assess-by-id:{id}`.
- **`ITenantAccessor`** resolves tenant/user; **Auth is OFF in dev** (configurable via `Auth:Enabled`) and falls back to `Auth:DevTenantId`.

### The grounding chain + advisory critics (recent, load-bearing — see ADR-030)
The chain is **lineage-anchored**: each stage is grounded server-side in the authoritative upstream artifact, revisions are durable+versioned, and freshness is derived from live content.
- **Opportunity→Blueprint fidelity:** `BlueprintService` re-fetches the full `PrioritizedItem` by `(ResearchArtifactId, OpportunityId)` via `OpportunityGroundingResolver` and renders it through the shared **`GroundingMaterialBuilder`** into `PromptBuilder.BuildBlueprint` (fixes the old name-only prompt). Client hand-paste (`SolutionDescription`) is the fallback.
- **Blueprint→Document / Execution grounding:** `PromptBuilder.BuildBlueprintContractSection` injects the blueprint's tech radar/topology/schema/endpoints/resilience as an authoritative "contract"; `execute-task` reuses the same block (blueprint or `AssessmentGrounding.Synthesise`d assessment).
- **Durable revisions + honest confidence:** blueprint patches persist a new version and **re-run `SolutionClassifierService`** (client-supplied confidence is ignored — it was spoofable).
- **Freshness:** `BlueprintFingerprint` (shared helper) keys the document cache, keys revision dedup, and is stamped on each document; `POST /api/documents/freshness` (+ `GET /api/artifacts/{id}/freshness`) reports `current | stale | unknown`.
- **Critics are ADVISORY and never gate:** `BlueprintReadinessService` (pre-blueprint clarifying questions), `DocumentReviewService` (post-document domain/opportunity/faithfulness), and `UseCaseAnalysisService` (pre-assessment) all clone the same shape (LLM cascade → structured JSON → heuristic fallback → cache) and only surface findings. **The only green-gate is the in-loop `DocumentGoalJudgeService`** during goal-directed document generation (ADR-008).

### Other subsystems
- **Documents** are goal-directed (`DocumentService`, up to 5 passes, judged by `DocumentGoalJudgeService`, structured-native with by-id `FixSectionAsync` — ADR-027). `PromptBuilder` + `LLMResponseParser` are the shared prompt/parse layer; `SelectionBankService`/`DocumentBankService` supply few-shot context; `PersonaRegistry` supplies personas.
- **Live research grounding** (`WebResearchEnricher`, RAG — ADR-022) feeds fact-heavy generation with `[S#]`-citable sources.
- **Mermaid self-healing** (`DocumentValidationService` + the validator sidecar): tiered learned-cache → deterministic repair → one-time LLM repair → cache. The Angular `MermaidDirective` also does a client-side deterministic repair pass at render time.
- **Frontend:** `WorkspaceStoreService` (root singleton, signals) is the hub for all state + HTTP; a tab-based `WorkspaceComponent` shell hosts standalone feature components; SSE is consumed via native `EventSource`; Markdown renders through a custom `MarkdownPipe` + `MermaidDirective`. The UI's `core/models/interfaces.ts` is the source of TS types but has historically drifted from the API — treat `Application/Contracts/*.cs` + the Scalar UI as the API contract source of truth.

## Conventions & invariants
- **Advisory critics never block generation** — only `DocumentGoalJudgeService` gates a document's "achieved" status; freshness/reviews/readiness are informational.
- **Fail-soft everywhere** — missing keys, store/sidecar/grounding failures degrade gracefully (heuristic output, `unknown` freshness, unchanged diagrams) rather than erroring.
- **LLM output is JSON-enveloped** (Gemini mime-type constraint); parsers tolerate fenced/bare/raw variants.
- **JSON wire format:** camelCase, nulls omitted, not indented; enums camelCase. CORS allows all origins in dev.
- **ADRs are canonical for "why"** — before changing an architectural pattern, check `docs/ADR/` (e.g. ADR-030 grounding chain, ADR-027 document pipeline, ADR-007 caching, ADR-003 SSE). When code and an ADR diverge, update the ADR.

## API surface
`POST /api/research`, `/api/generate-blueprint` (+ `/stream`, `PATCH /api/blueprint/{id}`, `/regenerate-topology`, `/chat`, `/generate-blueprint/readiness`), `/api/assessment/stream` (+ `PATCH`, `/chat`, `/analyze`), `/api/generate-document` (+ `/documents/fix`, `/documents/review`, `/documents/freshness`), `/api/execute-task`, `/api/generate-component-prompt`, `/api/artifacts/*` (list/get/versions/export/freshness/delete), `/api/events/model-status` (SSE), `/api/health`. Full request/response shapes: Scalar UI at `/scalar/v1` and `Application/Contracts/*.cs`.

The Heuristic Engine detects 9 industry verticals (Healthcare AI, Financial Technology, Legal Technology, Retail & E-Commerce, Real Estate, Education/EdTech, Local Services, Core Software & Tech, and an Enterprise AI Platform fallback) to return domain-specific offline output.
