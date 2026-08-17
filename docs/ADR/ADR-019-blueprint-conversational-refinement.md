# ADR-019 — Conversational Blueprint Refinement via Per-Panel Chat + Patch

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

Once the blueprint became the structured architecture contract ([ADR-018](ADR-018-blueprint-contract-documents-renderings.md)), users needed a way to refine its individual sections — "tighten this decision's rationale", "add a compliance quality attribute", "we'll buy the search layer instead" — without regenerating the whole blueprint (which is expensive and discards their other edits) and without forcing them to hand-edit every structured field.

Two interaction models were possible: (a) free-form inline editing of every field, or (b) a conversational assistant scoped to one panel at a time that proposes a structured change the user can apply. Inline editing alone is tedious for rationale-heavy fields and gives no AI assistance; whole-blueprint regeneration is destructive.

## Decision

**Each structured blueprint panel has its own AI chat that streams a response and proposes a typed patch the user applies to just that section.** Inline editing remains available for direct tweaks; chat is the assisted path.

- **Sectioned chat.** A panel's chat button opens `BlueprintChatDrawerComponent` (`features/architectural-blueprinter/blueprint-chat-drawer.component.ts`) with a section key — `arch-decisions`, `qa-scorecard`, `tech-radar`, `buy-vs-build` (see [ADR-020](ADR-020-domain-topologies-buy-build-regeneration.md)), etc. The drawer summarises the current section state so the model has grounded context.
- **Prompt.** `PromptBuilder.BuildBlueprintChat(...)` builds a `(System, User)` pair carrying the active section's label and its current structured data, and asks for a concise reply plus an updated version of that section only.
- **Streaming transport.** `POST /blueprint/{blueprintId}/chat` (`BlueprintEndpoints.HandleChatAsync`) returns the reply over SSE, reusing the same provider-cascade streaming pattern as blueprint generation — consistent with the project's SSE-over-WebSocket choice ([ADR-003](ADR-003-sse-over-websocket.md)).
- **Patch model.** Proposed changes are expressed as a `PatchBlueprintRequest` (typed per-section fields). The UI's `handleApplyPatch` applies the patch to the locally held blueprint; the server persists by re-`Set`-ting the cached `SystemBlueprint` under `bp-by-id:{id}`, so the contract every downstream document reads ([ADR-018](ADR-018-blueprint-contract-documents-renderings.md)) stays the authoritative, updated version.
- **Apply is explicit.** The model proposes; the user applies. Chat never mutates the blueprint silently.

## Consequences

### Positive
- Targeted, low-cost refinement: a single section is regenerated/edited without touching the rest of the blueprint or paying for a full recompile.
- Edits flow back into the cached contract, so documents generated afterward reflect the refinements automatically.
- Reuses existing infrastructure — the SSE streaming pattern, provider cascade, and `bp-by-id` cache keying — rather than introducing a new transport or store.
- Per-section scoping keeps prompts small and grounded, improving patch quality and reducing JSON-parse risk.

### Negative / Trade-offs
- Each new structured panel must be wired through the whole chain — section label + data in `BuildBlueprintChat`, a field on `PatchBlueprintRequest`, a `handleApplyPatch` branch, and a drawer summary case. Forgetting one link yields a panel whose chat can't apply changes.
- State lives in the cache, not a database; a cache eviction loses applied refinements along with the blueprint.
- Concurrent edits (inline edit + an in-flight chat patch on the same section) can race on the locally held blueprint; last write wins.

### Failure Modes
- If the model returns prose without a parseable section patch, `handleApplyPatch` has nothing to apply — the reply is shown but the blueprint is unchanged; the user must retry.
- A patch whose shape drifts from `PatchBlueprintRequest` is dropped at deserialization; the chat appears to "do nothing".
- Because persistence is a cache `Set` on `bp-by-id:{id}`, a regenerate-from-scratch or cache miss reverts to the unrefined blueprint.
