# ADR-021 — Use-Case-Driven Blueprint with Feasibility Analysis

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

Until now a blueprint could only be produced from a **research-selected solution** (Research → Select → Blueprint → Compile). There was no way to start from a free-form, real-world question such as: *"Our Azure landing page has capacity issues — can we replicate it to AWS and GCP? Is it easy or a big effort, what are the challenges and roadblocks, and will it actually resolve our capacity problem?"*

That class of question is a **feasibility / decision analysis**, which the existing `SystemBlueprint` did not model — it produces architecture artifacts (topology, schemas, arch decisions, quality attributes, tech radar, buy-vs-build) but nothing about effort, challenges, roadblocks, or a verdict on the user's core concern.

## Decision

Add a **Use-Case Analyzer**: the user types a scenario in plain English and receives a full architecture blueprint **plus** a side-by-side feasibility comparison. Three product decisions were confirmed with the user: (1) output is **blueprint + feasibility** (not one or the other), (2) entry is a **new dedicated tab**, (3) the analysis **compares options side by side**.

Rather than a parallel pipeline, the feature is one new optional input threaded through the **existing** blueprint pipeline ([ADR-018](ADR-018-blueprint-contract-documents-renderings.md)):

- **One request field.** `GenerateBlueprintRequest.UseCaseScenario` (string, optional). Its presence is the single switch for "use-case mode" — no new endpoint or service method; it reuses `POST /api/generate-blueprint/stream`.
- **One new model section.** `FeasibilityAnalysis` (`UseCase`, `Summary`, `PrimaryConcernVerdict`, `Options[]`) and `FeasibilityOption` (`Name`, `Verdict`, `Score`, `EffortEstimate`, `Challenges[]`, `Roadblocks[]`, `Recommendation`) on `SystemBlueprint.Feasibility` (nullable — null for research-driven blueprints). The result is a normal blueprint with one extra populated section, so every existing panel, document, and cache path keeps working unchanged.
- **Conditional prompt.** `PromptBuilder.BuildBlueprint` prepends the scenario as the authoritative problem statement and appends a `feasibility` object to the requested JSON only when the scenario is present; the token budget is raised (~4000 → ~5500) for that mode. `LLMResponseParser.GetFeasibility` deserializes it (returns null when absent).
- **Offline parity.** `LocalCompilationEngine.CompileBlueprint` takes the scenario and builds a heuristic `FeasibilityAnalysis` (`BuildFeasibility`) by keyword-detecting target platforms and intent (migrate/replicate/capacity), preserving the offline guarantee of [ADR-002](ADR-002-heuristic-engine-offline-fallback.md).
- **Refinable like every panel.** `feasibility` is a section key in the chat drawer and a field on `PatchBlueprintRequest`, so it streams + patches through the existing conversational-refinement mechanism ([ADR-019](ADR-019-blueprint-conversational-refinement.md)).
- **UI.** A new `use-case` tab hosts `UseCaseAnalyzerComponent` (scenario input → `store.generateBlueprintFromUseCase` → reuses the `compiledBlueprint`/`isGeneratingBlueprint`/`blueprintStreamText` signals). It renders the option comparison and a "View full architecture blueprint →" link; the Blueprint tab also gains a Feasibility panel guarded by `@if (bp.feasibility)`.

## Consequences

### Positive
- Net-new capability (free-form scenario → blueprint + feasibility) added by extending, not duplicating, the pipeline — streaming, provider cascade, caching, classifier, offline engine, and chat all reused.
- Feasibility data lives on `SystemBlueprint`, so it is automatically cacheable, document-consumable, and chat-refinable with no extra plumbing.
- Research-driven blueprints are completely unaffected: with no `UseCaseScenario`, no `feasibility` block is requested, parsed, or rendered.

### Negative / Trade-offs
- Use-case mode increases per-request token usage and JSON-schema surface, raising parse-fallback risk for the larger response ([ADR-006](ADR-006-json-only-llm-output.md)).
- The offline heuristic feasibility is a keyword-driven template — useful and grounded by domain, but far less specific than a live-LLM analysis; it is explicitly a fallback, not a substitute.
- A synthetic `solutionId` (client `crypto.randomUUID()`) and a scenario-derived `solutionName` are minted per submission; the cache key now includes `UseCaseScenario` to avoid collisions.

### Failure Modes
- If the model omits or malforms the `feasibility` object, `GetFeasibility` returns null and the panel simply doesn't render — the architecture blueprint still succeeds.
- A future structured sub-field added to feasibility must be wired through every layer (model, prompt schema, parser, offline engine, `PatchBlueprintRequest`, UI, chat summary) or it degrades silently — the same coupling noted in [ADR-020](ADR-020-domain-topologies-buy-build-regeneration.md).
