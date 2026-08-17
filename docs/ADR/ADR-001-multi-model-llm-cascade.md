# ADR-001 — Multi-Model LLM Cascade

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

MeridianStudio relies on large language models for five distinct operations: research, blueprint generation, document generation, component prompt generation, and mission suggestion. Each commercial LLM provider has independent quota limits, rate limits, and outage windows. Depending on a single provider means the application becomes unavailable whenever that provider hits a quota or experiences degraded service — which is common during peak usage for free or trial tiers.

Additionally, different providers have different strengths: Gemini 2.5 Flash is fast and cost-effective for structured research; Groq llama-3.3-70b is high-throughput for blueprint generation; Claude Sonnet produces the richest long-form narrative for documents. A fixed single provider cannot exploit these differences.

## Decision

All LLM-dependent operations route through a **priority-ordered cascade** of providers, implemented in `LLMOrchestrator`:

```
Gemini 2.5 Flash  →  Groq llama-3.3-70b  →  Claude Sonnet 4.6  →  Heuristic Engine
```

Providers are registered as `ILLMProvider` implementations in DI **in priority order** in `Program.cs`. The orchestrator iterates the registered list and:

1. Skips providers where `IsConfigured == false` (no API key set)
2. Attempts `provider.CompleteAsync(system, user, ct)`
3. On HTTP 429 (quota) or 503 (unavailable): logs the failure, rotates to next
4. On HTTP 400/401/422 (client error): does **not** retry — logs misconfiguration
5. On timeout or other exception: rotates to next
6. After all providers exhausted: invokes the Heuristic Engine (always succeeds)

The cascade order is not configurable at runtime — it is hardcoded in DI registration order to keep orchestration logic simple and deterministic.

Every API response carries a `modelUsed` field identifying which tier produced the result, enabling transparency and debugging.

## Consequences

### Positive
- Application degrades gracefully rather than failing when a provider has quota issues
- Zero user-visible errors from provider outages — the user receives a result from a lower tier
- Easy to extend: adding a new provider means adding one `ILLMProvider` registration
- `modelUsed` field gives operators visibility into which tier is doing work at any moment
- No API keys required — the Heuristic Engine guarantees a result

### Negative / Trade-offs
- Response quality varies between tiers — a Heuristic Engine result is deterministic but less creative than a live model
- Cascade order is static — there is no adaptive routing based on real-time provider performance metrics
- A misconfigured provider (wrong API key format) generates a 400 error and does not rotate — it surfaces as a misconfiguration warning rather than a silent skip
- Adding request-level provider preferences (e.g., "always use Claude for documents") is not supported without custom routing logic

### Failure Modes
- If all providers are unconfigured and the Heuristic Engine has a bug, there is no further fallback — this is a code defect, not an operational condition
- Cascade can mask slow providers: if Gemini takes 85 seconds before timing out, the user waits before Groq is tried
- `modelUsed` on a cached response reflects the original generation tier, not the current tier — this is by design but can mislead monitoring if cache hit rates are not tracked separately
