# ADR-009 — LLM-Generated Mission Suggestions

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

For goal-directed document generation (ADR-008) to work, users need a starting point for tone, goal, and criteria. The early design used a `MissionRegistry` with hardcoded options per template type — e.g., "Proposal" always showed the same 4 tone chips and the same 4 goal options regardless of what domain or solution the user was working with.

This was rejected for two reasons:
1. **Context-blindness:** A healthcare compliance proposal needs different tone and goal options than a retail e-commerce proposal. Static options do not adapt.
2. **Self-training dead end:** If suggestions are always the same, there is nothing to learn from user selections. The training signal only has value if suggestions vary by context and user choices indicate which variants work.

## Decision

`POST /api/mission-suggestions` calls the LLM to **dynamically generate contextual suggestions** for each combination of `(templateType, domain, solutionType)`:

- **4 tone options** — short label + full phrase, each grounded in the specific domain and solution type
- **4 goal options** — short label + 2–4 sentence goal text, describing different reader priorities
- **3 criteria sets** — named sets of 4 pass/fail evaluation criteria, differing by emphasis (commercial vs compliance vs technical vs domain-specific)

The prompt instructs the LLM: "Do not produce generic options. Every option must be specific to Healthcare AI + Azure Serverless" (or whatever the current context is).

Past user selections from `SelectionBankService` are injected into the prompt as few-shot context, so frequently-chosen combinations for similar contexts surface first in future suggestions.

Results are **cached for 1 hour** per `(templateType, domain, solutionType)` — long enough to avoid redundant calls within a session, short enough to refresh when selections accumulate.

A **heuristic fallback** (`LLMResponseParser.FallbackFor`) provides generic options if the LLM is unavailable, ensuring the UI is never blank.

## Consequences

### Positive
- Suggestions are genuinely contextual — a healthcare proposal gets compliance-focused tone and criteria options; a fintech blueprint gets regulatory options
- The self-training loop (ADR-010) is meaningful: when selections vary by context, each selection is a real signal
- Users can also "Copy & Refine" any suggestion — the dynamic starting point reduces the blank-page problem while still allowing full customisation
- 1-hour cache means the LLM is not called for every template type change within a session

### Negative / Trade-offs
- An extra LLM call per unique `(templateType, domain, solutionType)` combination — adds 3–8 seconds of loading time when suggestions are not cached
- LLM-generated suggestions can be inconsistent — the same context might produce different option sets on different calls, which could confuse users who expect stable UI
- If the LLM produces poorly differentiated options (all 4 tones sound similar), the selector adds UI noise without value. Prompt quality is the only guard against this.
- The 1-hour TTL means suggestions do not update during a session even if new user selections have accumulated

### Failure Modes
- If `MissionSuggestionService` takes longer than the user's patience (> 5 seconds), the skeleton loading state in the UI is shown — but the Generate button is blocked until suggestions load. A timeout + immediate fallback would improve the experience.
- The past-selections context injected into the prompt grows unbounded in the string — if `SelectionBankService` returns 5 recent entries, each with a long refined goal, the injected context could be several hundred tokens
