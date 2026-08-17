# ADR-022 — RAG Web Search Enrichment Pipeline for Live Trend Intelligence

**Status:** Accepted  
**Date:** June 2026  
**Deciders:** MeridianStudio team

## Context

The Research pipeline previously generated trends and opportunities from the LLM's training data alone (cutoff: mid-2025). For a trend-discovery tool this is a fundamental limitation: new funding rounds, product launches, regulatory changes, and emerging competitor moves after the training cutoff are invisible to users.

The fix is **Retrieval-Augmented Generation (RAG)**: fetch live web evidence before every LLM research call and inject it as grounded context into the prompt, so the model can cite current sources rather than extrapolating from static training data.

## Decision

### Architecture — Five Parallel Dimension-Aligned Search Groups

Rather than a single broad web query (which returns mediocre signal for all dimensions), the enricher fires **five targeted search groups in parallel**, each designed to gather evidence for specific scoring dimensions:

| Group | Evidences | Default provider | Override condition |
|---|---|---|---|
| Market / Business | Market Urgency, Business Value | Tavily `days=90` | `w_urgency ≥ 18` → `days=30` |
| Competitive | Competitive Gap | Tavily | `w_competitive ≥ 15` → **Serper** |
| Regulatory | Regulatory Tailwind | Tavily news | skipped when `w_regulatory < 8` |
| Implementation | Feasibility, AI Fitness, Difficulty | Tavily | domain-specific override (see below) |
| Pain Points | Pain Points section | Tavily | always Tavily |

Total latency = slowest single group (~600 ms). All groups fire simultaneously via `Task.WhenAll`.

### Adaptive Provider Routing

The routing engine reads domain name and dimension weights to select the most appropriate provider per group — **not hardcoded**:

- **Serper** (Google Search) for the competitive group when `w_competitive ≥ 15` — Google's index has superior coverage of vendor funding rounds and startup launches.
- **PubMed** (free, no key) for the implementation group when domain is Healthcare or Pharmaceutical and `w_feasibility ≥ 15` — surfaces clinical AI research as evidence for feasibility scoring.
- **GitHub Trending** (free, no key) for the implementation group when domain is IT Services, Telecommunications, or Manufacturing and `w_ai_fitness ≥ 15` — tech maturity signals from open-source adoption.
- **Tavily** for everything else — purpose-built for LLM augmentation, returns clean pre-processed summaries with semantic relevance scores.

If a provider key is missing, its `SearchAsync` returns `[]` immediately; the enricher degrades gracefully to training-data-only mode with no error.

### Subomain-Specific Queries

The enricher receives the **subdomain** (e.g., "E-Discovery Optimization") and parent **domain** (e.g., "Law") explicitly. Subdomain-level queries are far more targeted than broad domain queries. Each group runs 2 queries; results are deduplicated by URL and the top 15 by recency are injected.

### Use Case Tab Enrichment

The Use Case tab uses the same `WebResearchEnricher` via a different entry point (`EnrichUseCaseAsync`). The use case scenario is first classified via the LLM cascade (same orchestrator, no new keys) to extract domain, subdomain, and 5 targeted queries. All Use Case queries use Tavily only (competitive landscape irrelevant; no Serper needed).

### Prompt Injection Format

The live results are injected as a `LIVE MARKET INTELLIGENCE` block in the research prompt, labelled with fetch timestamp and source names. The LLM is instructed to prefer live signals over training data when available, and to cite live sources in pain point entries.

### Cache

Results are cached per `(subDomain, domain, weights)` SHA-256 hash for 60 minutes (`WebSearch:CacheTtlMinutes`). The dimension weights are included in the cache key so different weight profiles return independent result sets.

## Consequences

### Positive
- Research results are grounded in evidence from the past 30–90 days instead of a fixed 2025 training cutoff.
- Pain points can cite the specific article that evidenced the problem.
- Adaptive routing directs search budget where it matters most: high competitive-gap weight → Serper; healthcare feasibility → PubMed.
- No provider key = graceful degradation to training-data-only mode; no service disruption.
- PubMed and GitHub Trending add domain-specific depth at zero additional cost.

### Negative / Trade-offs
- Tavily: ~$0.04 per analysis; Serper: ~$0.002 per analysis — negligible at B2B scale but not free at high volume.
- 600 ms pre-search latency before the LLM call; visible as a brief "Searching live sources…" state.
- Five concurrent outbound HTTP calls increase dependency surface; any provider outage degrades the affected dimension group silently (returns `[]`).
- Cache key includes weights, so changing weights always re-fetches even for the same subdomain.

### Failure Modes
- Tavily API key revoked → all 4 Tavily groups return `[]`; live block absent from prompt; results degrade to training-data quality.
- Provider rate-limit → `EnsureSuccessStatusCode()` throws; `RunGroupAsync` catches and returns `[]` for that group; other groups unaffected.
- LLM ignores live context in prompt → no way to enforce; the rubric instructions mitigate this by anchoring scoring to cited evidence.
