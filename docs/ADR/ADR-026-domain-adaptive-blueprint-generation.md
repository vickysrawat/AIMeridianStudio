# ADR-026 — Domain-Adaptive Blueprint Generation

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

Blueprints were observed to read generically — not visibly grounded in the client's domain, the selected sub-domain, or the specific research trend the user picked. Investigation found the context was *received* but not *used* to specialise:

1. **LLM path (normal operation).** `PromptBuilder.BuildBlueprint` interpolated domain, sub-domain, and the opportunity description into the *user* prompt as plain labels, but the *system* prompt was a single static "Principal Cloud Architect … Fortune 500" persona — with **no domain specialisation**. By contrast, Research injects a rich domain-adaptive persona via `BuildResearchPersona` (HIPAA/PCI/FedRAMP expertise). The model was told *what* domain it was, but never told to *think like* that domain's architect.
2. **Offline path (heuristic fallback).** `CompileBlueprint` was called with only `(SolutionId, SolutionName, Domain)`; `SubDomain` and `SolutionDescription` were silently dropped. It branched on domain only — `BaseTopology`/`ArchDecisions`/`TechRadar`/`BuyVsBuild` varied per vertical, but `DatabaseSchemes`, `EndpointManifest`, and `ResilienceStrategies` were identical for every domain, and nothing used the sub-domain or the selected item.
3. **Cache key too coarse.** The key was `{SolutionId, SolutionName, Domain}`; re-running an item under a different sub-domain/opportunity returned a stale blueprint.
4. **Research signals dropped at the UI boundary.** `generateBlueprintStream` forwarded only description+rationale+realLifeValue; `integrationSteps` and the priority scores were discarded.

## Decision

**The blueprint is specialised to the domain, sub-domain, and the selected opportunity — both online and offline — and the cache key reflects that specificity.**

- **Domain-adaptive persona.** New `PromptBuilder.BuildBlueprintPersona(domain, subDomain)` (mirrors `BuildResearchPersona`) selects an architect specialisation by case-insensitive keyword match across the nine verticals — Healthcare (HIPAA, HL7/FHIR, SMART on FHIR), Financial (PCI DSS, ISO 20022, SOX, idempotent ledgers), Legal (privilege, ethical walls), Retail, Real Estate (MLS/IDX, PostGIS), Education (LTI 1.3, FERPA), Local Services, Core Software/SaaS, and an enterprise fallback — and pins the architecture to the specific sub-domain. It replaces the static system prompt; the user prompt now mandates that topology, data store, interoperability standards, compliance targets, tech radar, and buy-vs-build reflect the domain/sub-domain's real standards and the named opportunity.
- **Forward the dropped signals.** `GenerateBlueprintRequest` gains `IntegrationSteps?` (intended implementation approach) and `PrioritySignal?` (e.g. `"Urgency 9/10 · Difficulty 7/10 · Value 10/10"`); the UI populates them from the selected `PrioritizedItem`, and `BuildBlueprint` adds them to the opportunity-context block.
- **Offline engine uses the full context.** `CompileBlueprint` now accepts `subDomain` and `solutionDescription`, resolves the domain profile on the richest available signal, names the sub-domain and opportunity in the core scenario/topology, and the three previously-generic sections are now domain-aware: a domain-specific table per vertical (e.g. `*_patients`, `*_ledger_entries`, `*_listings`), domain-specific endpoint rows (e.g. `/fhir/Observation`, `/api/v1/payments`), and domain hardening notes (RTO/RPO + audit durability for regulated verticals).
- **Cache key includes sub-domain + opportunity.** The blueprint cache key adds `SubDomain` and a 120-char-truncated `SolutionDescription`, so different sub-domains/opportunities no longer collide on a stale entry.

## Consequences

### Positive
- The same trend under two sub-domains — and two trends under one sub-domain — now produce visibly different, domain-expert blueprints, online and offline.
- The blueprint path reaches parity with Research's persona strategy ([ADR-024](ADR-024-it-services-persona-strategy.md)) and makes the "domain-specific topologies" promise ([ADR-020](ADR-020-domain-topologies-buy-build-regeneration.md)) true across all panels, not just topology/decisions.

### Negative / Trade-offs
- A second domain-keyword map now lives in `PromptBuilder` alongside the canonical one in `LocalCompilationEngine.ResolveProfile`; the two must be kept roughly in sync (kept separate deliberately to avoid coupling the LLM layer to the offline engine, matching the existing `BuildResearchPersona` pattern).
- The new optional request fields stay nullable to satisfy `TreatWarningsAsErrors`; the offline `SystemBlueprint` coalesces them to empty strings.
- The finer-grained cache key reduces hit rate slightly (intended: it trades a few cache hits for correctness across sub-domains).

### Failure Modes
- The persona keyword match is heuristic; an unusual domain string falls back to the generic enterprise persona (still better than before, never worse).
- Offline domain accents are illustrative scaffolding, not a substitute for the LLM output — they only surface when all providers are exhausted.

This refines [ADR-018](ADR-018-blueprint-contract-documents-renderings.md) (the blueprint remains the architecture contract; it is now better grounded) and complements [ADR-020](ADR-020-domain-topologies-buy-build-regeneration.md).
