# ADR-018 — Blueprint is the Architecture Contract; Documents are Renderings of It

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

The trigger was a direct question: *"We already generate documents of different types — what value does the Blueprint add?"* Code inspection confirmed a real overlap. The Technical Specification, Detailed Design, Developer Handbook, and Governance/ADR documents each **independently regenerated** SQL schemas, REST endpoint tables, and architecture narratives — the same artifacts the blueprint had already produced.

The root cause was at the document API boundary: `DocumentService` passed only `BlueprintContext` — `InputGuard.Sanitize(...)` truncated `coreScenario` to ~1,500 characters — into the document prompt. The blueprint's `DatabaseSchemes`, `EndpointManifest`, `ResilienceStrategies`, and `SolutionType` were never read by the document generator. So a Technical Specification "database schema" was invented from a 1,500-char narrative rather than from the blueprint's actual DDL. Blueprint and documents diverged silently, and users had no mental model for which to trust.

The full analysis lives in [`docs/blueprint-tab-strategic-redesign.md`](../blueprint-tab-strategic-redesign.md); this ADR records the decision and its current (implemented) state.

## Decision

**The blueprint is the single source of truth — a structured, machine-readable architecture contract. Documents are audience-shaped renderings of that contract, not independent generators.** (Analogy: the blueprint is the OpenAPI spec; documents are the Swagger UI / SDKs / mocks derived from it.)

Three changes implement this:

1. **Data pipeline — documents consume the full structured blueprint.**
   `DocumentService.GenerateGoalDirectedAsync` retrieves the cached blueprint via `cache.TryGet<SystemBlueprint>("bp-by-id:{BlueprintId}", out var blueprint)`, falling back to the legacy `BlueprintContext` only on a cache miss. `PromptBuilder` emits a `BLUEPRINT CONTRACT (authoritative — embed these values verbatim, do not regenerate)` section embedding the real `EndpointManifest`, `DatabaseSchemes`, and `ArchDecisions` (capped, not 1,500-char truncated). Template instructions for technical/detailed-design types say to embed, not invent.

2. **The WHY layer — structured decisions and quality targets** added to `SystemBlueprint` (`Domain/Models/SystemBlueprint.cs`):
   - `ArchDecision(Decision, ChosenApproach, Rationale, AlternativesConsidered[], Risks[])` — 4–6 records (data store, decomposition, API style, auth, resilience, AI/ML pattern). This is the layer only the blueprint can own, because the reasons are architectural decisions, not audience renderings.
   - `QualityAttribute(Attribute, Target, Measurement)` — 5–8 rows (availability, latency, throughput, retention, security, RTO …). Documents embed exact figures instead of fabricating percentages.
   Both are requested in the LLM JSON schema, deserialized in `LLMResponseParser.ParseBlueprint`, populated by `LocalCompilationEngine` for the offline path, and exposed on the UI `SystemBlueprint` interface.

3. **UI refocus** (`architectural-blueprinter.component.ts`): primary panels now show executive scenario, solution type, the Architecture Decision table, the Quality Attribute scorecard, and a Technology Radar. Implementation artifacts (SQL DDL, endpoint manifest, ASCII topology) are demoted into a collapsed "Implementation Detail" accordion — kept, but no longer the headline.

Document roles are clarified rather than removed: Governance/ADR renders the blueprint's `ArchDecisions` for an audit audience; Executive Summary embeds `QualityAttributes`; Technical Specification/Detailed Design embed the endpoint/schema data. Market Analysis and Proposal are unchanged.

## Consequences

### Positive
- Documents stop diverging from the blueprint — the endpoint table in a Technical Spec is the blueprint's table, embedded, not a second invention.
- The blueprint now answers *what + why*; documents answer *what it means to you*. The two stop looking like ten redundant documents.
- Structured `ArchDecisions`/`QualityAttributes` are reusable across multiple document types and are directly editable (see [ADR-019](ADR-019-blueprint-conversational-refinement.md)).

### Negative / Trade-offs
- Documents are now coupled to the blueprint's cached presence; a cache miss silently degrades to the legacy 1,500-char `BlueprintContext` path, which reintroduces divergence.
- The blueprint JSON schema is larger and more constrained, raising token usage and the chance of a parse fallback (see [ADR-006](ADR-006-json-only-llm-output.md)).
- Demoting SQL/ASCII to an accordion is a deliberate opinion about what an EA-level view should lead with; users who came for raw DDL must expand to find it.

### Failure Modes
- If the blueprint is evicted from `PayloadCache` before a document is generated, documents fall back to truncated context and quietly diverge again — the exact bug this ADR set out to fix. Cache TTL and `bp-by-id` keying must outlive the document workflow.
- Out of scope by explicit decision (do **not** build): C4/Mermaid interactive diagrams, blueprint versioning/diff, multi-blueprint portfolio view, ArchiMate/draw.io import — these either invert the contract→rendering value proposition or aren't justified yet.
