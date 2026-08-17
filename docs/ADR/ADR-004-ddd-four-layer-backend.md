# ADR-004 — DDD Four-Layer Backend Architecture

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

As MeridianStudio grew from a simple research API to a multi-feature platform (research, blueprint, execution, documents, prompts, mission suggestions, self-training), the codebase needed a structural pattern that:
- Kept LLM prompt logic, caching, and parsing out of endpoint handlers
- Made it easy to add new features without touching unrelated code
- Enforced clear ownership: "who knows about what"

Without a deliberate structure, all logic tends to accumulate in endpoint handlers or service classes that grow to thousands of lines mixing orchestration, LLM calls, caching, and domain logic.

## Decision

The API follows a **four-layer DDD layout**:

```
Domain/          ← entities, value objects, sealed records — no dependencies
Application/     ← use cases, services, interfaces, request/response contracts
Infrastructure/  ← LLM clients, caching, example bank, local engine, SSE broadcaster
API/             ← Minimal API endpoint mappers, input validation, HTTP concerns
```

**Dependency rule:** outer layers depend on inner layers; inner layers never depend on outer ones.

- `Domain/` has zero dependencies — it is plain C# records with no framework references
- `Application/` depends on `Domain/` and `Infrastructure/` interfaces only
- `Infrastructure/` depends on `Domain/` and external packages (HttpClient, System.Text.Json)
- `API/` depends on `Application/` interfaces — never calls `Infrastructure/` directly

Key structural choices:
- All LLM prompt construction lives in `PromptBuilder` (static, Infrastructure)
- All LLM response parsing lives in `LLMResponseParser` (static, Infrastructure)
- Application services (`DocumentService`, `BlueprintService`, etc.) orchestrate the flow but do not build prompts or parse JSON
- Endpoints are thin: validate input → call service → return result

## Consequences

### Positive
- Adding a new feature (e.g., mission suggestions) is self-contained: new domain model + application service + infrastructure service + endpoint, with no changes to existing code
- Testability: domain models are pure records; application services can be tested with mock LLM providers; parsers can be tested with raw string fixtures
- Clear answer to "where does this logic go?" for every new piece of code
- `PromptBuilder` and `LLMResponseParser` as static classes make all LLM-specific logic findable in one place

### Negative / Trade-offs
- More files and folders than a flat architecture — a five-feature app has ~40 files instead of ~10
- The static `PromptBuilder` / `LLMResponseParser` pattern means adding a new operation requires editing shared files, which can create merge conflicts on a team
- No explicit domain events or aggregate roots — the "DDD" is structural, not fully tactical

### Failure Modes
- If a developer adds a direct `HttpClient` call inside an Application service (bypassing Infrastructure), the layer boundary silently breaks. Code review is the only guard.
- `PromptBuilder` accumulating all 6+ operation builders in one file (~700+ lines) risks becoming hard to navigate — consider splitting per-feature if it exceeds 1,000 lines.
