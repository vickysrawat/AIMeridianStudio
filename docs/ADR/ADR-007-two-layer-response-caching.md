# ADR-007 — Two-Layer Response Caching (Memory + Disk)

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

LLM API calls are expensive in both latency (5–20 seconds) and token cost. For a demo and learning application, the same research query, blueprint, or document is frequently generated multiple times — either by the same user exploring the app or by different users exploring the same domain. Without caching, every interaction makes a live LLM call regardless of whether an identical result already exists.

Two options were considered:
- **A) In-memory only:** Fast, simple, lost on restart — requires re-generating everything after each server restart
- **B) Database-backed:** Durable, queryable, but adds an external infrastructure dependency (SQL or Redis) that increases operational complexity

## Decision

`PayloadCache` (`Infrastructure/Cache/PayloadCache.cs`) implements a **two-layer cache**:

**L1 — In-Memory:** `ConcurrentDictionary<string, CacheEntry>` — sub-millisecond reads, lost on restart

**L2 — Disk JSON:** One file per cache key at `{CacheDiskPath}/{sha256_key}.json` — survives restarts, checked on L1 miss

Cache keys are computed as `SHA-256(JSON-serialised request object)` — identical requests produce identical keys.

**Per-operation TTLs** (configurable via `appsettings.json`):
- Research: `Cache:Research:TtlHours` (default 24h)
- Blueprint: `Cache:Blueprint:TtlHours` (default 24h)
- Document: `Cache:Document:TtlHours` (default 24h)
- Task: `Cache:Task:TtlHours` (default 24h)
- Mission suggestions: 1 hour (hardcoded — these should refresh frequently)

**Heuristic Engine results are never written to cache.** This is deliberate: if a user generates a result while offline and then API keys are configured, the next request should hit a live LLM rather than returning the cached heuristic result.

**`isRerun: true`** on research and document requests explicitly evicts the cache key before generation, forcing a fresh LLM call.

## Consequences

### Positive
- Repeat requests return in < 1ms instead of 5–20 seconds
- Disk layer survives API server restarts — frequent restarts during development do not destroy all cached results
- No external infrastructure required — the disk cache is a local folder, suitable for a development and demo context
- SHA-256 key computation ensures identical requests always find the same cache entry, even across process restarts

### Negative / Trade-offs
- Disk cache is local — it does not share across multiple API server instances (relevant for future horizontal scaling)
- Disk I/O on L2 miss adds ~5–20ms latency compared to a Redis hit
- Cache files are plain JSON — they are human-readable but not encrypted; sensitive content in LLM responses (if any) would be visible on disk
- No automatic eviction when disk space is constrained — files accumulate until TTL expires or manual cleanup

### Failure Modes
- If the disk cache directory is not writable (permissions issue), L2 writes fail silently — L1 still works, but cache is lost on restart
- SHA-256 key collisions are theoretically possible but astronomically unlikely for request-sized inputs
- Stale `modelUsed` on cached responses: a response cached when Gemini was active will show `"Gemini (gemini-2.5-flash)"` as `modelUsed` even if Gemini is now disabled — this is by design (preserves the original attribution) but can confuse operators
