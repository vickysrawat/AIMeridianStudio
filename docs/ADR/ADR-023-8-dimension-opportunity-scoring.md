# ADR-023 — 8-Dimension Weighted Opportunity Scoring with Domain-Adaptive Profiles

**Status:** Accepted  
**Date:** June 2026  
**Deciders:** MeridianStudio team

## Context

The original `PrioritizedItem` had three raw scores — `urgency`, `difficulty`, `value` — that were displayed as bars without a coherent framework. Users had no way to express priorities like "this client cares more about regulatory compliance than speed-to-market" or "we only want opportunities our firm can actually deliver".

The research tab needed a principled multi-dimensional scoring model that:
1. Reflects the specific perspective of an **IT services firm** (not a generic market observer)
2. Allows **user-controlled weighting** so different clients or practice areas can shift priorities
3. Produces a **single comparable composite score** for ranking

## Decision

### 8 Dimensions

| Dimension | What it measures | Composite direction |
|---|---|---|
| Business Value | Revenue/cost impact for the buyer | positive |
| Market Urgency | How fast buyers are acting NOW | positive |
| Feasibility | Can an IT services firm deliver in <18 months? | positive |
| Competitive Gap | How underserved by existing vendors | positive |
| Implementation Difficulty | How hard to build/deliver | **inverted** (`10 − score`) |
| Regulatory Tailwind | Compliance/regulation forcing adoption | positive |
| Strategic Fit | Fits the firm's existing practice areas | positive |
| AI Fitness | AI is genuinely better than rules/traditional code | positive |

Implementation Difficulty is inverted so higher difficulty = lower composite contribution, matching the natural expectation that "easier to build = better opportunity".

### Composite Formula

```
Composite = Σ (dimensionScore × weight/100)
```
where Implementation Difficulty contributes `(10 − score) × weight/100`.

All dimension scores are 1–10 integers supplied by the LLM. The composite is computed **client-side** from the LLM's scores and the user's weights — no extra API call.

### Priority Badges

| Badge | Condition |
|---|---|
| 🔴 Critical | Composite ≥ 8.5 AND Market Urgency ≥ 8 |
| 🟠 High | Composite ≥ 7.0 |
| 🟡 Medium | Composite ≥ 5.0 |
| ⚪ Low | Below 5.0 |

Critical requires *both* a strong composite AND high urgency — preventing opportunities from earning Critical status on strategic value alone when market timing is poor.

### User-Adjustable Weights (sum = 100)

Weights are integers controlled by 8 sliders in a slide-in drawer. The UI enforces sum = 100 before analysis can run. Server normalises proportionally if the client sends weights that don't sum to 100 (graceful error recovery).

### Domain-Adaptive Default Profiles

Rather than a single neutral default, domains are pre-assigned to one of **5 weight profiles** based on their dominant characteristics:

| Profile | Key characteristics | Domains |
|---|---|---|
| Highly Regulated | High `regulatoryTailwind` (20%) | Healthcare, Pharmaceutical, Financial Services, Insurance, Government, Tax |
| AI-Native Tech | High `feasibility` (20%) + `aiFitness` (16%) | IT Services, Telecommunications, Manufacturing |
| Professional Services | High `businessValue` (21%) + `regulatoryTailwind` (13%) | Law, Audit, Advisory, HR & Workforce |
| Operations & Industry | High `businessValue` (22%) | Retail, Supply Chain, Energy, Real Estate, Construction, Agriculture, Travel, Media |
| Balanced | Neutral across all 8 | Education & EdTech (default fallback) |

All profiles sum to 100. A "Reset to domain defaults" link in the drawer restores the profile weights.

### LLM Scoring via Rubrics

The prompt injects **exact 1-line rubrics** for each dimension so the LLM scores consistently across runs:

> `Business Value: 10 = proven >$10M annual savings or revenue at scale; 1 = unclear or marginal ROI.`

The live market intelligence from ADR-022 explicitly grounds the scores: the model is instructed to cite live search results when assigning scores.

### Backward Compatibility

The 8 dimension score fields are **optional** on `PrioritizedItem` — existing code that doesn't provide weights receives items with null dimension scores. The UI falls back to displaying the legacy `urgency`, `difficulty`, `value` bars when dimension scores are absent.

## Consequences

### Positive
- Single comparable composite score enables ranking across very different opportunity types.
- User-controlled weights make the tool useful across practice areas with different investment criteria.
- Domain-adaptive profiles provide intelligent defaults so the first analysis is already calibrated.
- Difficulty inversion is mathematically correct and matches user intuition.
- Client-side composite computation avoids additional API round-trips.

### Negative / Trade-offs
- 8 simultaneous dimension scores increase the LLM prompt's JSON schema size and token output.
- LLMs tend to regress dimension scores toward the 5–7 range; extreme scores (1–2, 9–10) require the rubrics to be calibrated carefully.
- The "sum = 100" constraint, while disciplined, can frustrate users who want to raise one slider without knowing which others to lower.
- Weights persisted per subdomain in localStorage can become stale if the user's firm changes strategy.

### Failure Modes
- LLM ignores rubric anchors → dimension scores cluster around 5–6; composite differentiates less; badges skew toward Medium.
- User sets all weights to 0 except one → that single dimension becomes a 100% filter; unusual but valid.
- Client-side composite formula diverges from server-side rubric instructions if they fall out of sync during future updates.
