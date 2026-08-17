# ADR-002 — Heuristic Engine as Offline Fallback

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

The cascade in ADR-001 guarantees a result even when all live LLM providers fail. But the final fallback needs to be truly unconditional: synchronous, dependency-free, and incapable of throwing a transient error. Calling another external service as the final fallback would reintroduce the same availability risk.

Furthermore, MeridianStudio is a learning and demo application. It must work fully during development, demos, and presentations where internet access to API providers is unreliable or API keys have not been configured. Without an offline fallback, the application is unusable without live keys.

## Decision

`LocalCompilationEngine` (`Infrastructure/LocalEngine/LocalCompilationEngine.cs`) is a **stateless, synchronous, purely procedural engine** that covers all five operations (research, blueprint, task execution, document, component prompt) for **9 hardcoded industry verticals**:

1. Healthcare AI
2. Financial Technology
3. Legal Technology
4. Retail & E-Commerce
5. Real Estate & Property Management
6. Education & EdTech
7. Local Services
8. Core Software & Tech
9. Enterprise AI Platform (catch-all fallback)

Domain detection uses `string.Contains` keyword matching — no regex, no external calls, no allocation beyond string operations. Each domain profile contains:
- Static competitor insights (4 per domain)
- Static prioritised solution items (pool of 10, 5 returned per request)
- Static tech stack, DB pattern, and architecture description strings

The engine runs in **< 1ms** on any hardware and has no failure mode other than a code defect.

Results from the Heuristic Engine are **never cached** to disk — this ensures a live LLM result is used on the next request if a provider comes back online, without having to manually evict.

## Consequences

### Positive
- Application works with zero API keys configured — full feature exploration is possible
- Zero-latency fallback (< 1ms) — users experience no loading delay in offline mode
- Deterministic output — the same input always produces the same result, which helps reproduce issues and build regression tests
- No network dependency — functions on an airplane, in a restricted enterprise network, or behind a firewall

### Negative / Trade-offs
- Output is generic and domain-specific only to the extent the keyword matching fires — cross-domain solutions (e.g., "AI for HR in a hospital") get a single-domain result
- The 9 vertical profiles are hardcoded — adding a new vertical requires a code change and redeploy
- Heuristic outputs lack the nuance, specificity, and creativity of live LLM responses — they are scaffolds, not production content
- Code is large (~2,800 lines of static string data) — changes to domain profiles are tedious

### Failure Modes
- Keyword matching uses `string.Contains` — a very short keyword like "ai" could match unintended domains if not carefully ordered. Detection priority order mitigates this: more specific verticals are checked first.
- The catch-all "Enterprise AI Platform" fires for any unrecognised keyword — it produces valid but generic output, which could mislead users into thinking their domain was recognised
