# ADR-029 — Research-Driven White Paper with Scoped Citations

**Status:** Accepted
**Date:** July 2026
**Deciders:** MeridianStudio team

## Context

The White Paper feature was a **generic artifact concatenator**. The user picked arbitrary saved artifacts; the service fed the LLM **one-line summaries** (competitor names + feature-gap fragments, pain-point titles) and asked for a generic "Problem / Solution / Comparative" structure. It ignored the rich material the platform had already gathered — competitor **strategic playbooks**, pain-point descriptions, opportunity **rationale / real-life value / integration steps / feasibility / 8-dimension scores** ([ADR-023](ADR-023-8-dimension-opportunity-scoring.md)), and **live web sources** ([ADR-022](ADR-022-rag-web-search-enrichment.md)) — and it was not anchored to a specific domain·subdomain·scenario. The output was generic and thin.

The intended deliverable is a decision-grade paper that answers, for one specific domain·subdomain·scenario:

1. **What's happening** in the domain/subdomain (market / trends),
2. **What other companies are working on** (competitive landscape),
3. **What we can do** (differentiated approach + recommendations).

Two failure modes had to be designed out:

- **Fabricated market/competitor/sizing facts** — the same confident-misinformation hallucination class [ADR-015](ADR-015-competitor-grounding-market-analysis.md) and [ADR-027](ADR-027-trustworthy-structured-document-pipeline.md) address for documents, but the white paper had none of that grounding.
- **The "generic" feel** — a single broad search (or no search) yields consensus prose. An LLM cannot *manufacture* competitive edge; the edge comes from proprietary inputs + targeted current evidence (the same premise as [ADR-027](ADR-027-trustworthy-structured-document-pipeline.md)).

## Decision

**A white paper is synthesized from a resolved upstream artifact — a Research run, a selected opportunity, or a use-case Assessment — not from arbitrary free-picked inputs, and it is grounded with domain-aware live research, with citations scoped by claim type.**

- **Driven modes + resolver.** `WhitePaperRequest` carries `ResearchArtifactId?` (with an optional `OpportunityId?` to focus the sharpest scenario), `AssessmentId?`, and the legacy `ArtifactIds?` kept as a **secondary** manual mode; `Title?` (derived when absent) and `GroundWithFreshResearch = true`. The mode is inferred from which driver is set; the endpoint validates that **at least one driver** is present (not the old `title + artifactIds`). `WhitePaperService` resolves the driver → `(domain, subDomain, scenario focus, competitors, pain points, opportunities, existing sources)`: research/opportunity via `IArtifactStore`, assessment via `PayloadCache` (`assess-by-id:{id}`, see [ADR-028](ADR-028-standalone-assessment-artifact.md)).

- **Rich material, not one-liners.** The prompt receives full competitor `StrategicPlaybook`, pain-point detail (description / segment / severity), and the focus opportunity's narrative + feasibility + 8-dimension scores — the proprietary inputs that make the paper defensible rather than consensus prose.

- **Guardrail 1 — scoped citations (the key decision).** Every **empirical** claim (market / competitor / sizing / trend) must carry a `[S#]` citation or degrade to a visible `[REQUIRED: …]` placeholder — never a fabricated number. **Recommendations and analysis** ("What we can do") are the author's synthesis and are **deliberately not citation-gated.** The *scoping* is the decision: gating every sentence turns the analytical section — the point of the paper — into placeholder-soup; gating nothing invites fabrication. Reuses [ADR-027](ADR-027-trustworthy-structured-document-pipeline.md)'s `[S#]` / `[REQUIRED:]` instruments and the `SourceTraceabilityRule` / `VendorCapabilityRule` / `NoAssumptionRule` prompt constants.

- **Guardrail 2 — domain-aware queries, not generic fan-out.** When `GroundWithFreshResearch` is on and live search is available, `WebResearchEnricher.EnrichDocumentAsync(...)` fires **intent-shaped** queries — competitors, market sizing/trends, differentiation/positioning — targeted at the domain·subdomain and **seeded with the research run's competitor names as `vendors`**. Its results merge with the run's existing sources into a single `[S#]`-numbered set. Reuses the [ADR-022](ADR-022-rag-web-search-enrichment.md) pipeline and its 30-day grounding cache ([ADR-007](ADR-007-two-layer-response-caching.md)).

- **Fixed section structure.** `# Title` · `## Executive Summary` · `## Domain & Subdomain Landscape` (what's happening — cited) · `## Competitive Landscape` (what others are working on — cited) · `## The Opportunity / Scenario` · `## What We Can Do — Recommendations & Approach` (analysis; not gated) · `## Sources`. Built by `PromptBuilder.BuildWhitePaper`.

- **Provenance + reuse.** The call routes through the LLM cascade with heuristic fallback ([ADR-001](ADR-001-multi-model-llm-cascade.md) / [ADR-002](ADR-002-heuristic-engine-offline-fallback.md)); an `OutputProvenance` (model, source count, confidence, live sources) is attached and surfaced in the UI (confidence badge + source chips). The paper is persisted as a `whitepaper` Document artifact and passes through the same Mermaid repair + markdown backslash sanitizer as other documents. **Entry points carry the scenario:** "Generate White Paper" on the research-run header, on a per-opportunity card, and on the use-case assessment; the White Paper tab auto-generates from that context with a "Ground with fresh research" toggle.

## Consequences

### Positive

- The paper answers the actual question — what's happening, who is doing what, and what we can do — for a *specific* domain·subdomain·scenario, built from proprietary research material instead of one-line fragments.
- Empirical claims are attributable or honestly flagged, while the analytical recommendation section stays readable because it is not citation-gated.
- Domain-aware, competitor-seeded queries fix the "generic" feel **and** produce the sources Guardrail 1 needs; the 30-day cache makes a re-run fast and cheap.
- Little net-new infrastructure: grounding, `[S#]`/`[REQUIRED:]` citations, caching, provenance, and the cascade are all reused from existing ADRs.

### Negative / Trade-offs

- Citation-scoping is a judgment encoded in the prompt: an empirical claim that lands inside the "recommendations" section escapes the `[S#]` requirement. Accepted deliberately — gating recommendations produced placeholder-soup; the executive/landscape/competitive sections carry the empirical burden.
- A fresh-grounding fee per paper when the toggle is on — mitigated by the shared 30-day cache and the opt-in toggle.
- Confidence is derived from **source coverage, not truth** (the same "attributable ≠ true" caveat as [ADR-027](ADR-027-trustworthy-structured-document-pipeline.md)); high-stakes figures still warrant human review.
- The legacy arbitrary-artifact mode remains — a second code path to keep working.

### Failure Modes

- **Grounding unavailable** (no keys / provider 503) → fail-soft to the research payload; more `[REQUIRED:]` placeholders; provenance reflects the heuristic engine / lower confidence. *(Observed live: a transient Gemini 503 fell back to the Heuristic Engine and produced a paper; a retry produced the full grounded version.)*
- **Research artifact evicted / opportunity id not found** → the resolver falls back to the run-level scenario, or errors clearly for the manual path.
- **No goal-judge pass.** Unlike goal-directed documents ([ADR-008](ADR-008-goal-directed-document-generation.md)), the white paper's confidence is provenance-based, not a judge verdict. A natural future step is to route the paper through the [ADR-027](ADR-027-trustworthy-structured-document-pipeline.md) substantiation verifier so each `[S#]` is checked against the retrieved source text.

This applies [ADR-027](ADR-027-trustworthy-structured-document-pipeline.md)'s grounding + `[S#]`/`[REQUIRED:]` substantiation instruments to a new artifact and adds **claim-type citation scoping**; reuses [ADR-022](ADR-022-rag-web-search-enrichment.md) (domain-aware live search) and its renumbered sibling [ADR-028](ADR-028-standalone-assessment-artifact.md) (the assessment as a driver + persisted source pool); extends [ADR-015](ADR-015-competitor-grounding-market-analysis.md) (competitor grounding, previously market-analysis-only) to the white paper; and reuses [ADR-001](ADR-001-multi-model-llm-cascade.md) / [ADR-002](ADR-002-heuristic-engine-offline-fallback.md) (cascade + heuristic fallback) and [ADR-007](ADR-007-two-layer-response-caching.md) (caching).
