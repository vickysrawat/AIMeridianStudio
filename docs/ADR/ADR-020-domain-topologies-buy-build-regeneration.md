# ADR-020 — Domain-Specific Topologies, Buy-vs-Build, and Explicit Topology Regeneration

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

After the blueprint became the architecture contract ([ADR-018](ADR-018-blueprint-contract-documents-renderings.md)), three gaps remained:

1. **Generic topology.** `LocalCompilationEngine.BuildBaseTopology` used a single ASCII template for all nine domains — Healthcare, FinTech, and Legal all rendered the same "API gateway → service → database → event bus" shape, which is misleading.
2. **No buy-vs-build guidance.** Blueprints didn't help users decide which components to buy (SaaS/product) versus build — a critical early-project decision.
3. **Topology drift.** When a user changed architecture decisions, the technology radar, or project context (including via chat, [ADR-019](ADR-019-blueprint-conversational-refinement.md)), the topology diagram didn't reflect those changes.

The implementation plan is [`load-the-blueprint-plan-federated-lobster.md`](../../.claude/plans/load-the-blueprint-plan-federated-lobster.md); this ADR records the decisions and their two explicit user choices.

## Decision

### 1. Topologies are domain-specific
`BuildBaseTopology` switches on `DomainProfile.Name` (same pattern as `BuildArchDecisions`), giving each of the nine verticals an ASCII diagram showing its real integration layers — e.g. Healthcare gets FHIR R4/HL7 gateway, CDS Hooks, DICOM/PACS, pgvector clinical store; FinTech gets ISO 20022 payment rails, fraud ML, Kafka, Iceberg audit store; Legal gets a DMS connector (iManage/NetDocuments), CMIS, matter-boundary isolation.

### 2. Buy-vs-Build is a first-class structured panel, generated up front
A new typed record models the decision:
```csharp
public sealed record BuyVsBuildOption(
    string Component, string BuyOption, string BuyRationale,
    string BuildApproach, string BuildRationale,
    string Recommendation /* Buy | Build | Hybrid */, string RecommendationReason);
```
`SystemBlueprint.BuyVsBuild` holds 5–7 entries (auth, primary data store, API gateway/BFF, search, notifications, observability, AI/ML inference as applicable).

**Explicit user decision: Buy-vs-Build is LLM-generated during the initial blueprint streaming**, not lazily on demand — it's part of the `BuildBlueprint` JSON schema (~600 extra tokens, within the 4,000-token budget), parsed by `LLMResponseParser.GetBuyVsBuild`, and produced by `LocalCompilationEngine.BuildBuyVsBuild` for the offline path. The UI renders it as Panel 07 with a recommendation badge (green/amber/blue for Buy/Build/Hybrid) and a chat hook ([ADR-019](ADR-019-blueprint-conversational-refinement.md)).

### 3. Topology regeneration is an explicit button, not automatic
**Explicit user decision: topology is regenerated only when the user clicks a button**, never auto-regenerated on every edit.
- `PromptBuilder.BuildTopologyRegeneration(bp)` asks a "Principal Cloud Architect" to return *only* a Markdown ASCII diagram, grounded in the current `ArchDecisions` (top 5), `TechRadar`, `ProjectNotes`, and `QualityAttributes` (top 3).
- `BlueprintService.RegenerateTopologyAsync(blueprintId, ct)` streams the result (reusing the provider cascade), patches the cached blueprint (`bp-by-id:{id}` → `bp with { BaseTopology = … }`), and yields a final `complete` event.
- `POST /blueprint/{blueprintId}/regenerate-topology` exposes it over SSE ([ADR-003](ADR-003-sse-over-websocket.md)); the UI shows streaming text under the Implementation Detail accordion's System Topology sub-section, then renders the updated diagram.

## Consequences

### Positive
- Topologies are credible per vertical instead of one misleading generic shape; the heuristic offline engine produces domain-specific output too.
- Buy-vs-Build surfaces a high-value early decision directly in the contract, reusable by documents and refinable via chat.
- Explicit regeneration resolves drift on demand without the token cost and surprise of auto-regenerating on every edit; the user controls when the diagram catches up to their decisions.

### Negative / Trade-offs
- Generating Buy-vs-Build during initial streaming raises every blueprint's token cost and JSON-schema surface, even when the user never looks at that panel.
- Explicit regeneration means the topology can sit **stale** relative to edited decisions until the user remembers to click regenerate — correctness is traded for cost/control.
- Nine hand-authored domain topologies (and matching offline Buy-vs-Build tables) are static strings to maintain in `LocalCompilationEngine`; new verticals need new switch arms.
- Topology regeneration returns Markdown, not JSON, so it uses fence-stripping extraction rather than the standard JSON parser — a second, slightly different response-handling path.

### Failure Modes
- A cache miss on `bp-by-id:{id}` makes `RegenerateTopologyAsync` unable to load the source blueprint; regeneration fails for an otherwise-visible blueprint.
- If the regeneration response contains no fenced block, extraction yields empty/garbled topology; the diagram must guard against blank output.
- A new structured field (like `BuyVsBuild`) that isn't wired through every layer — model, prompt schema, parser, local engine, `PatchBlueprintRequest`, UI, chat summary — degrades silently to an empty panel.
