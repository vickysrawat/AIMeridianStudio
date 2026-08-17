# ADR-030 — Lineage-Anchored Generation Chain: Server-Side Grounding, Durable Revisions, and Advisory Critics

**Status:** Accepted — implemented (P0–P1 + G3-A shipped; follow-ons noted)
**Date:** July 2026
**Deciders:** MeridianStudio team

## Context

A code audit + architecture brainstorm (two design workflows) verified that the end-to-end chain — research/opportunity → blueprint (±revision) → document → execution → white paper, plus the use-case → Assessment branch — had **thin, opaque seams** that capped document quality and left the chain incoherent:

- **G1 (refuted):** blueprint generation received only `{SolutionId, SolutionName, Domain, SubDomain?, SolutionDescription?, IntegrationSteps?, PrioritySignal?}`. The rich `PrioritizedItem` (rationale, real-life value, 8-dimension scores, pain points, competitor playbooks, feasibility) **never reached the prompt** — the same one-liner reduction already eliminated in the white paper, living at the *front* of the chain.
- **G2 (refuted):** `PatchBlueprintAsync`/`RegenerateTopologyAsync` wrote **only `PayloadCache`** (lost on restart, no version); the classifier **never re-ran on patch**, and `SolutionTypeConfidence` was **client-spoofable**; `ParentArtifactId` was set nowhere; no descendant knew it was stale.
- **G3 (confirmed):** `execute-task` carried no design id and grounded nothing — a standalone simulator divorced from the blueprint it "executes."
- **G4:** the only reviewer (`DocumentGoalJudgeService`) checked the selected goal/criteria — nothing audited a finished document against its **domain / originating opportunity / faithfulness**.
- **G5:** the opportunity→blueprint path had **no clarifying-question step** (the assessment path already had `UseCaseAnalysisService`).

Async background document generation — an earlier proposal — was demoted to **P3 infrastructure**: it delivers little quality alone and only *hosts* the critics later.

## Decision

**Adopt a lineage-anchored chain: every stage is grounded server-side in the authoritative upstream artifact, every revision mints a durable version, freshness is derived from live content, and each stage gains an ADVISORY critic that surfaces drift without gating.** Concretely:

- **Server-side grounding, not client payloads (G1).** `BlueprintService` re-fetches the `PrioritizedItem` by `(ResearchArtifactId, OpportunityId)` and renders it through a shared `GroundingMaterialBuilder` (extracted from `WhitePaperService.BuildMaterialBlock` — one canonical formatter, no drift) into `PromptBuilder.BuildBlueprint`. Both ids enter the cache key so re-grounding never serves a stale blueprint. The client hand-paste (`SolutionDescription`) remains a fail-soft fallback. The re-fetch is a reusable `OpportunityGroundingResolver`.

- **Durable, honest revisions (G2).** `PersistingBlueprintService.PatchBlueprintAsync` now persists a **new artifact version** (`ForBlueprintRevision`, dedup-keyed on the content fingerprint). On patch the heuristic classifier **always re-runs** on the patched content and the **client-supplied `SolutionTypeConfidence` is ignored** (an explicit `SolutionType` override still wins at 1.0) — killing the spoof and keeping confidence honest.

- **Pull-based freshness (G2).** `BlueprintFingerprint` is a shared helper hashing the grounding-relevant fields. It keys the document cache, keys revision dedup, and is **stamped on each generated document**. `GET /api/artifacts/{id}/freshness` recomputes the current blueprint's fingerprint and reports `fresh | stale | unknown` — derived from live content, never a stored flag, so it can't drift.

- **Advisory critics that never gate green (G4, G5).** Two critics clone the proven `UseCaseAnalysisService` shape (LLM cascade → structured JSON → heuristic fallback → cache): `BlueprintReadinessService` (pre-blueprint clarifying questions, critiquing the re-fetched opportunity) and `DocumentReviewService` (post-document domain / opportunity-fidelity / faithfulness). **The only green-gate remains the in-loop `DocumentGoalJudgeService`** — critics surface drift the author (or a later job stage) can act on; they never block.

- **Grounded execution + lineage (G3-A).** `ExecuteTaskRequest` gains optional `BlueprintId`/`AssessmentId`; `TaskExecutionService` injects the **same** `BuildBlueprintContractSection` DESIGN CONTEXT that grounds documents, folds the fingerprint into the task cache key, and `ForTask` sets `ParentArtifactId` + `blueprint:`/`assessment:` tags — closing opportunity→blueprint→document→task into one traceable lineage. Upstream feedback stays **advisory** until durable revision persistence (above) exists.

## Consequences

### Positive
- Blueprints (and every downstream document) finally specialise to the *actual* opportunity — the highest-leverage quality lever, fixed at the front of the chain.
- Confidence is trustworthy (re-classified, un-spoofable); revisions are durable and versioned; documents can report staleness after a blueprint changes.
- Off-domain / unfaithful drift is surfaced post-generation; thin opportunities get sharpening questions pre-generation.
- Execution consumes the real design and joins the lineage.
- Near-total reuse: one grounding-material builder, one fingerprint, one advisory-critic shape, `IArtifactStore` versioning + `ArtifactProjection` + `PersistenceGuard` for all durability. Little net-new infrastructure.

### Negative / Trade-offs
- More server surface (two critic services, a grounding resolver, a freshness endpoint, a revision projector) — mitigated by cloning existing patterns.
- Critics add an opt-in LLM call each; freshness is pull-based (the UI must poll) in the first cut.
- Server-side grounding makes the API the trusted grounding source; API consumers that never persist research fall back to the hand-paste path.

### Failure Modes
- Grounding/opportunity not persisted → re-fetch returns null → fail-soft to `SolutionDescription` (no crash, lower fidelity).
- Blueprint evicted from cache → freshness returns `unknown` (never a false `stale`).
- Assessment base artifacts remain cache-only until `PersistingAssessmentService` parity lands (a tracked follow-on) — after a restart, assessment-grounded re-fetch degrades.
- A critic LLM failure returns empty findings / neutral readiness — advisory, so it never blocks.

### Follow-on refinements (post-acceptance)

- **Broadened honest-confidence signal (extends G2).** The heuristic classifier originally read only `BaseTopology + CoreScenario + EndpointManifest` — blind to the **tech radar** and **arch decisions**, which carry the most keyword-dense type signal (concrete tech names the classifier keys on). `SolutionClassifierService` now offers a `Classify(SystemBlueprint)` overload that builds its corpus from the full design (topology, scenario, endpoints **plus** tech radar, arch decisions, project notes, buy-vs-build, quality attributes); `BlueprintService` uses it on both generate and patch. The patch re-classify trigger widened accordingly, so editing any design-bearing field now moves confidence. The keyword-count formula is unchanged; the explicit `SolutionType` override still wins at 1.0 and client confidence is still ignored. Trade-off: confidence now shifts on more edits and may re-detect the type when the radar/decisions imply a different architecture — intended, more-honest behaviour.
- **Pre-generation context, one continuous field (extends G1/G5).** `GenerateBlueprintRequest` gains an optional `ProjectNotes` — user-authored constraints/context, populated in the UI by one-click **Apply** on the readiness critic's suggestions. It is woven into `BuildBlueprint`/`BuildOpportunityReadiness` as an authoritative PROJECT CONTEXT block and **persisted onto the resulting blueprint's `ProjectNotes`**, so pre-gen answers pre-populate the post-gen Project Context card and flow into every downstream document/chat (unifying pre- and post-generation context rather than dead-ending). This closes the readiness loop: *Check readiness → Apply gaps → Compile*. Fail-soft: empty notes change nothing.

- **LLM-primary solution-type classification (supersedes the heuristic-only classifier).** The keyword heuristic mis-classified projects whose dominant pattern isn't keyword-dense — e.g. a codebase/document *ingestion → roadmap* system read as **Event-Driven** purely because of incidental queue/trigger plumbing — and the taxonomy lacked common patterns entirely. Now the **generation LLM emits `solutionType` + `solutionTypeConfidence`** in the blueprint JSON, and `BlueprintService.ResolveSolutionType` picks the type in trust order **caller override (1.0) → LLM answer (canonicalised, confidence clamped 0.5–0.95) → keyword heuristic**. Because the classification is produced *server-side by the generation model*, it is **not client-spoofable** — preserving G2's honest-confidence property while fixing accuracy. `SolutionClassifierService` gained a broader taxonomy (Batch Processing, Streaming/Real-Time, RAG/Knowledge Retrieval, Agentic AI, Monolith, Static Site, Mobile App, Desktop App, GraphQL API) plus a `Canonicalize` mapper that validates the LLM's label / resolves synonyms. **Patch no longer re-runs the heuristic** (reversing the re-classify-on-patch above): a section edit preserves the existing, trustworthy type; only an explicit `patch.SolutionType` changes it (at 1.0), so a correct type can't be clobbered by the weaker heuristic on an unrelated edit.

This refines [ADR-008](ADR-008-goal-directed-document-generation.md) (the goal loop stays the sole green-gate), applies [ADR-027](ADR-027-trustworthy-structured-document-pipeline.md)'s grounding/faithfulness discipline across the whole chain, extends [ADR-015](ADR-015-competitor-grounding-market-analysis.md) and [ADR-029](ADR-029-driven-white-paper-with-citation-scoping.md) (shared `GroundingMaterialBuilder`), evolves [ADR-018](ADR-018-blueprint-contract-documents-renderings.md)/[ADR-019](ADR-019-blueprint-conversational-refinement.md) (revisions are now durable, versioned, freshness-tracked), and reuses [ADR-007](ADR-007-two-layer-response-caching.md) (caching), [ADR-014](ADR-014-di-lifetime-strategy.md) (scoped services), and the artifact store. The async background-job model remains a deferred P3 delivery vehicle that will host the critics as pipeline stages.
