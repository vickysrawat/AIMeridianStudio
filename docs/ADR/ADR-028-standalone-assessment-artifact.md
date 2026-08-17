# ADR-028 — Standalone Assessment Artifact for the Use Case Workflow

> **Note:** Renumbered from ADR-022 to resolve a number collision with
> [ADR-022 — RAG Web Search Enrichment Pipeline](ADR-022-rag-web-search-enrichment.md).
> Content is unchanged.

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

The Use Case tab originally forced every scenario through the application-development `SystemBlueprint` (topology, schemas, endpoints, tech radar, buy-vs-build) plus a feasibility add-on, then nudged the user toward "build a new app" ([ADR-021](ADR-021-use-case-driven-blueprint-feasibility.md)). That structure is wrong for assessment-style use cases — multi-cloud strategy, migration feasibility, options trade-offs, capacity questions — where the user wants a decision-ready answer shaped by their *Expected Outcome*, not an invented system design.

The blocker to simply producing a different artifact was that **Documents could only be generated from a blueprint** (`GenerateDocumentRequest.BlueprintId`, resolved from the `bp-by-id:` cache). Any new artifact had to keep the Documents/Prompts/Presentation flow working.

## Decision

**The Use Case workflow produces a first-class `Assessment` artifact — not a blueprint — and the document pipeline is generalised to ground a document in an Assessment OR a blueprint.**

- **`Assessment` model** (`Domain/Models/Assessment.cs`): echoed brief (use case, context, problem, objective, scope, expected outcome) + a **concise** outcome — `ExecutiveSummary`, adaptive `Sections` (Markdown, shaped by the Expected Outcome), `Recommendations`, `Risks`, `NextSteps`, an optional `Feasibility` options comparison (reuses `FeasibilityAnalysis`), and `RecommendedDocuments` (each Expected Outcome mapped to a document template). No application-development fields exist on it.
- **Concise assessment, deep documents.** The assessment stays tight; the heavy per-outcome deliverables are generated on demand as Documents via the `RecommendedDocuments` list. This keeps each LLM call focused and lets the user pull only the deliverables they need.
- **Intake — both modes.** A free-form scenario *and* a structured six-field brief feed `AssessmentRequest`; `PromptBuilder.BuildAssessment` instructs a consultant persona to *produce the Expected Outcome* and explicitly not assume a build.
- **Same pipeline, new artifact.** `AssessmentService` mirrors `BlueprintService` (provider cascade, heuristic fallback via `LocalCompilationEngine.CompileAssessment`, SSE streaming), cached under `assess-by-id:{id}`. Endpoints: `POST /api/assessment/stream`, `PATCH /api/assessment/{id}`, `POST /api/assessment/{id}/chat`. Refinement reuses the existing chat drawer via a new `basePath` input.
- **Documents consume either source.** `GenerateDocumentRequest` now carries `BlueprintId?` **or** `AssessmentId?`. `DocumentService.ResolveGroundingBlueprint` loads the blueprint, or synthesises a grounding `SystemBlueprint` from the assessment (its narrative lands in `CoreScenario`; app-dev fields marked "not applicable"). The UI normalises this with a `documentSource` computed signal (assessment-first, else blueprint); a recommended deliverable pre-selects its template and auto-runs in Document Studio.

## Consequences

### Positive
- The use-case deliverable finally matches the request: a strategy/feasibility/roadmap answer shaped by the Expected Outcome, with no forced app-dev skeleton.
- Documents/Prompts/Presentation keep working unchanged for blueprints, and now also work from assessments — one document engine, two artifact kinds.
- Maximum reuse: provider cascade, offline engine, caching, SSE, chat drawer, and `FeasibilityAnalysis` are all shared rather than duplicated.

### Negative / Trade-offs
- A genuinely new artifact type (model, service, endpoints, parser, offline path, UI) is more surface area than extending the blueprint would have been.
- `GenerateDocumentRequest.BlueprintId` became optional; a `SourceId` helper and an "exactly one of blueprint/assessment" validation rule are now required to keep the non-null contracts that `TreatWarningsAsErrors` enforces.
- Two grounding paths in `DocumentService` (real blueprint vs. synthesised-from-assessment) must stay behaviourally aligned.

### Failure Modes
- If an assessment is evicted from `assess-by-id:` before a document is generated, grounding falls back to the truncated `BlueprintContext` prose — lower fidelity, same as the blueprint cache-miss path.
- The auto-run from a recommended deliverable fires before mission suggestions load, so it uses the single-pass (legacy) document path; the user can re-run with the goal-directed mission flow for higher quality.
- This evolves [ADR-021](ADR-021-use-case-driven-blueprint-feasibility.md): the use-case tab no longer writes `SystemBlueprint.Feasibility`, so that blueprint field and the use-case branch of `BuildBlueprint` are now dead for this flow (left inert; `FeasibilityAnalysis` itself is reused by `Assessment`). The [ADR-018](ADR-018-blueprint-contract-documents-renderings.md) contract→renderings model is preserved — the assessment is simply a second kind of contract.
