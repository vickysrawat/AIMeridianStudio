# ADR-027 — Trustworthy Structured-Native Document Pipeline

**Status:** Accepted — implementation pending (phased A → B)
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

Two problems surfaced in document generation and editing:

1. **Editing corrupts the document.** Clicking *Fix* on a failed criterion produced **duplicate sections in random places** and a **scorecard that churned** (different criteria pass/fail each click). Root cause: the document is a **free-text Markdown blob the LLM rewrites**, merged back by *heading-text matching* ([DocumentSectionMerger]), and the judge **re-evaluates all criteria non-deterministically on every pass** while a single fix runs the full multi-pass loop. The corruption is inherent to "unstructured text + heading-merge + full re-judge," so it must be removed at the model level, not patched.
2. **Hallucinated facts about named vendors.** A competitor matrix asserted "Azure has limited access to … AI/ML services" — unsupported, partly wrong, written from training memory. Document generation never does live search; competitor grounding fires only for market-analysis with supplied insights; and the honesty rules ban inventing competitor *names/figures* but **not qualitative capability claims** about named vendors.

Strategically, an LLM cannot *manufacture* competitive edge — it emits consensus knowledge. The edge comes from **proprietary inputs, speed/completeness, and human judgment**; the product's moat is the **trustworthiness** of grounded, auditable, honest-about-gaps documents — not the prose. The pipeline must therefore make proprietary insight legible, complete, grounded, and current, and be honest where it cannot.

## Decision

**Documents are born structured; Markdown is a deterministic render of that structure. Honesty is enforced by grounding + substantiated citations + human sign-off, layered on in two phases.**

**Phase A — structured-native generation + by-id fix (fixes the corruption).**
- A document is `StructuredDocument { Sections[], Criteria[], Sources[] }`; each `DocumentSection` has a stable `Id`, `Heading`, `Body`, mapped `CriterionIds`, and `CitationIds`. `DocumentRenderer.Render` produces the Markdown `Content`.
- Generation is **hybrid by size**: one structured call for small/medium templates; **outline-then-fill** per section for large templates (detailed-design, developer-handbook) to avoid the 8k-output truncation on Groq/Claude; a truncation guard + single-section fallback either way.
- A *Fix* is a **single targeted node operation**: replace the section **by id** (duplicates impossible), **re-judge only the affected criteria** (the fixed one plus any criterion mapped to the changed section), freeze the rest. The criteria stack is **frozen** at generation. The fix is **stateless** (the client holds the structured doc), removing the need for a server-side plan cache. Markdown parsing survives only as a one-time migration for legacy docs.
- Prompt-only vendor-honesty fixes ship here: a **`VendorCapabilityRule`** (any qualitative/comparative claim about a named vendor must be `[S#]`-cited or become `[REQUIRED:]`), an empty-insights competitor constraint, and a judge that fails uncited/false-cited vendor claims.

**Phase B — honesty + coherence (flag-first; after A).**
- **Grounding source by workflow:** blueprinter path → the SystemBlueprint; use-case path → the Assessment narrative + a persisted research-source pool (no blueprint exists).
- **Grounding instrument:** **Gemini native Google-Search grounding** (a separate, non-JSON call that reads real pages and returns `groundingSupports`), with **Tavily deep-fetch** (general topic, rich excerpts) as fallback — never the news-tuned mode.
- **Substantiation-based citations:** a `[S#]` is allowed **only when the retrieved source text supports the claim**; otherwise `[REQUIRED:]`. A cited-but-unsupported claim is treated as **fabrication-with-false-citation** and fails the judge. A citation means *attributable to retrieved text*, **not** *verified true* — so the **clickable source link + "grounded as of \<date\>" are surfaced beside every citation** (inline, in a rendered Sources list, and in the review panel), giving the reviewer one-click verification, and **high-stakes claims (vendor capability, pricing, regulatory, financial) require explicit human sign-off** before the document is `ReviewComplete`.
- **Reconcile** is an on-demand, terminal whole-document consistency pass. The gate is **flag-first** (never auto-deletes) and calibrated in flag-only mode first.
- **Cost control:** a **shared, tiered grounding cache** keyed on the grounding subset `{domain, subDomain, vendor-set, prompt-version}` (NOT the full payload), reusing the two-layer `PayloadCache` ([ADR-007]) with a stale-file sweep and single-flight lock; capability facts ~30 days, pricing ~1–3 days; plus a **per-operation "Ground in live research" checkbox** so the user pays the grounding fee only when they opt in. Cost ceiling is soft (measure via telemetry first).

## Consequences

### Positive
- Duplicate/placement/churn bugs are removed **by construction**; fixes are deterministic, scoped, and **cheaper** than today's multi-pass loop.
- Vendor/opportunity documents become **defensible**: every specific claim is cited to substantiating source text or honestly flagged, with a human certifying high-stakes facts.
- The grounding cache converts the dominant cost from *per-document* to *per-unique-query-per-window*, keeping spend low even at volume.

### Negative / Trade-offs
- **Generation-contract change** (the LLM must emit a structured `sections` array) — mitigated by the existing JSON-envelope discipline + a single-section fallback.
- A **per-grounded-document fee** (Gemini Google-Search grounding) on fact-heavy templates; grounding *coverage* depends on Gemini availability (correctness never does — the honesty floor holds).
- Real added surface area (two phases, a verifier, a grounding provider, a review UI); deliberately phased so Phase A ships value alone.
- **"Attributable ≠ true"**: the system grounds and flags; truth certification is delegated to a human sign-off — by design, not omission.

### Failure Modes
- Large structured output could truncate → handled by outline-then-fill + truncation guard.
- Verifier false positives → flag-first (never deletes correct content); originals recoverable.
- Grounding/search unavailable → `[REQUIRED:]` placeholders, never invented facts.
- First fix on a legacy (pre-structured) document runs a one-time bootstrap parse — the riskiest step; validated before applying.

This refines [ADR-008](ADR-008-goal-directed-document-generation.md) (the goal-directed loop now operates on a structured document with a frozen criteria stack), generalises [ADR-015](ADR-015-competitor-grounding-market-analysis.md) (grounding extends to any named-vendor claim, substantiation-verified), extends [ADR-022](ADR-022-rag-web-search-enrichment.md) (web-search enrichment now feeds document generation, primarily via Gemini grounding) and [ADR-028](ADR-028-standalone-assessment-artifact.md) (assessment sources are persisted and reused as the use-case grounding pool), and reuses [ADR-007](ADR-007-two-layer-response-caching.md) and [ADR-018](ADR-018-blueprint-contract-documents-renderings.md) (the blueprint remains the grounding contract).
