# ADR-025 — Structured Subdomain Research Workflow with 22-Domain Taxonomy

**Status:** Accepted  
**Date:** June 2026  
**Deciders:** MeridianStudio team

## Context

The original Research tab used a **keyword search input** — users typed free-form terms and the LLM guessed the domain via the heuristic engine's `ResolveProfile` keyword scan. This had three problems:

1. **Ambiguous classification**: "landing page on Azure" matched Retail, Core Software, or IT Services depending on keyword order — all wrong for a cloud-infrastructure problem.
2. **No sub-domain precision**: broad keyword searches couldn't target specific problem areas like "E-Discovery Optimization" within Law.
3. **No persistent research agenda**: every session started from scratch; users had to retype the same keywords repeatedly.

## Decision

### 22-Domain Taxonomy with 7 Subdomains Each

A structured taxonomy replaces keyword guessing. The 22 domains are:

Law, IT Services, Tax, Advisory, Audit, Healthcare, Financial Services, Real Estate, HR & Workforce, Retail & E-Commerce, Manufacturing, Government & Public Sector, Education & EdTech, Media & Entertainment, Energy & Utilities, Supply Chain & Logistics, Insurance, Travel & Hospitality, Construction, Agriculture, Telecommunications, Pharmaceutical.

Each domain has exactly 7 subdomains. The taxonomy is loaded from `DomainSuggestions` (existing API endpoint) and displayed in the Domain Settings modal as an expandable accordion with checkboxes.

### Research Areas Left Pane

The left pane of the Research tab becomes a **persistent research agenda** — a list of `SelectedSubdomain` objects (domain + subdomain + results status + stale flag) grouped by parent domain. This replaces the keyword input.

Each subdomain row shows:
- A status dot (● green = results ready, ● dim = not yet analyzed)
- A sliders icon (⊞) that opens the dimension weights drawer for that specific subdomain
- A ✕ remove button

### Per-Subdomain Dimension Weights

Dimension weights ([ADR-023](ADR-023-8-dimension-opportunity-scoring.md)) are **stored per subdomain** in a `Map<string, DimensionWeights>` in the Angular store. When a subdomain is added, it pre-loads the domain-adaptive weight profile as the default. The user can adjust weights via the slide-in drawer (same pattern as the Blueprint Section Chat, [ADR-019](ADR-019-blueprint-conversational-refinement.md)) and click Accept.

### Explicit Analyze Action

The user clicks an **Analyze** button scoped to the active subdomain. The frontend sends `{ subDomain, domain, weights }` to `POST /api/research`. The keyword field is preserved as `subDomain` for backward compat with the heuristic engine fallback.

### Custom Entry Fallback

Two text inputs (Domain + Sub-domain) with an Add button allow users to type entries not in the taxonomy. Custom entries are added to `selectedSubdomains` with `defaultWeightsForDomain` (balanced profile if no match). The research request flows identically to taxonomy-selected subdomains.

### Persistence

`selectedSubdomains` (domain+subdomain pairs) is persisted to localStorage alongside `preferredDomains`. On page load, `_loadPersistedPrefs` restores both signals, so the Research Areas panel survives page refreshes. `addSubdomain` and `removeSubdomain` call `_persistPrefs` immediately after each change.

### Use Case Tab Integration

When the user enters a free-text scenario in the Use Case tab, the LLM cascade (Groq-first for speed) classifies it against the 22-domain taxonomy, returning domain + subdomain + confidence. The detected domain drives the search provider routing ([ADR-022](ADR-022-rag-web-search-enrichment.md)) and the weight profile pre-load. The user sees a "Detected: IT Services › Cloud Resource Optimization [Change ▾]" chip above the Generate button and can correct before running.

### Keyword Search Disposition

The keyword text input is **removed** from the Research tab. The `ResearchRequest.Keywords` field is kept for backward compatibility (set to the subdomain name) and for the heuristic engine offline path which still uses keyword-based `ResolveProfile` detection. When `SubDomain` is present in the request, it takes precedence; `Keywords` acts as a fallback.

## Consequences

### Positive
- Subdomain-level queries to the web search providers ([ADR-022](ADR-022-rag-web-search-enrichment.md)) are far more targeted than broad domain keywords.
- The research agenda persists across sessions — opening the app shows exactly where the user left off.
- Per-subdomain weights mean different subdomains (e.g., E-Discovery vs. Contract Lifecycle Management) can have different priority profiles simultaneously.
- The 22-domain taxonomy eliminates ambiguous keyword classification; domain detection is now user-controlled and explicit.

### Negative / Trade-offs
- Users must select from the taxonomy before analyzing — the immediate gratification of typing any keyword and getting results is gone.
- The custom entry fallback exists but is less discoverable than a prominent search box.
- Domain settings modal now serves dual purpose (research area selection + general domain preferences) — two concerns in one UI component.
- 22 × 7 = 154 subdomains is a large selection space; the accordion UI helps but is still dense.
- Per-subdomain weight maps live in Angular memory only (Map, not signal); they are not included in persistence alongside `selectedSubdomains` — weights reset between sessions.

### Failure Modes
- `discoveredDomains()` is empty when the user opens the modal (no API call yet) → modal shows empty accordion; user must click Discover Domains first.
- Restoring `selectedSubdomains` from localStorage before `discoveredDomains` loads → `addSubdomain` succeeds (it doesn't require discovered state), but the weights map must be re-populated from localStorage pairs.
- Removing a subdomain that was actively being analyzed → analysis result is discarded; the user must re-add and re-analyze.
