# ADR-015 — Competitor Intelligence Grounding for Market Analysis Documents

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

The `market-analysis` document template describes the competitive landscape for a solution. Without grounding, an LLM generates plausible-sounding but fabricated competitor names, feature comparisons, and strategic claims. This is the highest-risk hallucination failure mode in the application — it produces confident-looking misinformation about real companies.

The research phase (`POST /api/research`) already produces authoritative competitor data: `competitorName`, `featureGap`, `impactScore`, and `strategicPlaybook` for four real competitors in the domain. This data is grounded (for live LLM calls) or deterministic (for the Heuristic Engine). It is the only trusted source of competitor facts in the system.

Two options were considered for document generation:
1. **Regenerate competitor analysis from scratch** — let the document generation LLM identify competitors independently
2. **Inject the research-phase competitor data** — pass it explicitly into the document generation prompt and instruct the LLM not to invent alternatives

## Decision

For `market-analysis` documents, `DocumentService.BuildCompetitorSections()` extracts competitor data from the `GenerateDocumentRequest.CompetitorInsights` field and injects it into both the generation prompt and a hard constraint:

**Injected section** (`competitorSection`):
```
COMPETITOR INTELLIGENCE (sourced from live market research — treat as authoritative):
  • Epic Systems
    Feature gap vs our solution: ...
    Strategic impact: 8.5/10
    Recommended playbook: ...
  • [3 more competitors]
```

**Injected constraint** (`competitorConstraint`):
```
CRITICAL — COMPETITOR GROUNDING:
  • Use ONLY the competitors listed in COMPETITOR INTELLIGENCE above.
  • Do NOT invent, assume, or add any other competitor names.
  • Do NOT generalise (e.g. 'major cloud providers') — name each competitor explicitly.
  • Quote the specific feature gaps and playbooks provided — do not rephrase as generics.
```

This constraint is appended at the end of the system prompt where LLMs give it highest weight.

The two sections are built **once** before the iteration loop and reused across all passes (including patch passes 2–5) to ensure competitor data is not diluted or replaced by hallucination during iteration.

For all other template types (`executive-summary`, `technical-specification`, `proposal`), `competitorSection` and `competitorConstraint` are empty strings — the injection is a no-op.

## Consequences

### Positive
- Market analysis documents cite real competitor names from the research phase — eliminates the most damaging hallucination class
- The constraint's explicit "do not invent" phrasing has proven effective in practice: providers follow it even when they have strong prior training on specific competitors
- Data flows unambiguously: research → competitive insight → market-analysis document. The UI passes `competitorInsights` through `GenerateDocumentRequest` explicitly, making the dependency visible
- The constraint is reused across all iteration passes — a corrected document cannot drift back to hallucinated competitors on pass 2

### Negative / Trade-offs
- The UI must pass the `competitorInsights` array from the research response when requesting a market-analysis document. If the user skips research and jumps directly to document generation, `CompetitorInsights` is null and the grounding sections are empty — the LLM falls back to its training data
- The constraint adds ~150 tokens to every market-analysis generation prompt — a small but non-zero cost
- The four competitors from the research phase are fixed — the LLM cannot extend or update the list even if it knows of a more relevant competitor. This is intentional: hallucination risk outweighs completeness

### Failure Modes
- If the research phase used the Heuristic Engine, `competitorInsights` contains static profiles, not live data — the document will ground to heuristic content. This is still better than full hallucination but should be labelled clearly (the `modelUsed` field on the research response indicates the source)
- Very long `strategicPlaybook` or `featureGap` values in `CompetitorInsights` (if an LLM generated verbose entries) can push the competitor section beyond 600 tokens, competing with the document structure instructions for context window priority
