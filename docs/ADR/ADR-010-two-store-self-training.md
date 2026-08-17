# ADR-010 — Two-Store Self-Training Architecture

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

MeridianStudio needed a self-improvement mechanism so that suggestion quality and document quality get better with use — without a dedicated ML pipeline, a database, or manual curation.

Two types of signal are available:
1. **User selection signal** — what tone/goal/criteria combination did the user choose for this domain + template type? This is available even before the document is generated and even if generation fails.
2. **Document quality signal** — did the generated document achieve its goal? This is only available after generation completes, and only when the goal was actually achieved.

These two signals have different quality, latency, and value profiles. Mixing them into a single store would require a single quality threshold for both, which would either exclude too many selections (setting the bar at the document quality level) or include too many poor documents (setting it at the selection level).

## Decision

**Two separate disk-based JSON stores:**

**`SelectionBankService`** (`Infrastructure/ExampleBank/selections/{templateType}.json`):
- Records **every user selection** — immediately when "Generate Document" is clicked, before generation starts
- **No quality threshold** — every selection is a training signal regardless of the document outcome
- Max 20 entries per file, rolling (oldest evicted first)
- Used to inform future `POST /api/mission-suggestions` calls — the LLM sees what past users chose in similar contexts and surfaces those options prominently
- Trains the **suggestion layer** — improves what options are shown before generation

**`DocumentBankService`** (`Infrastructure/ExampleBank/documents/{templateType}.json`):
- Records documents **only when `goalAchieved == true`** (GoalAchievementPct ≥ 80%)
- Max 5 entries per file; on overflow, the entry with the lowest `goalAchievementPct` is evicted
- Used as **few-shot examples** in `BuildDocument` prompts — the LLM sees excerpts of previously successful documents with the goal that was set
- Trains the **generation layer** — improves document quality on first pass over time

The two stores have different decay rates: selections accumulate quickly (20 entries replaced in days of active use) while quality documents accumulate slowly (only successful generations qualify). This naturally means document examples stay fresh longer.

## Consequences

### Positive
- Self-improvement loop runs entirely server-side — no external ML pipeline, no labelling effort
- The system genuinely improves with use: popular selections rise in suggestion rankings; successful document patterns inform future generations
- Storing the `goalUsed` field alongside each example means the LLM understands *what goal* produced *what structure*, enabling context-aware few-shot learning
- Separating the stores means a failed generation does not corrupt the quality bank

### Negative / Trade-offs
- Training signals accumulate locally per server instance — horizontal scaling would require a shared store (Redis, S3, etc.) to pool signals across instances
- `wasRefined: true` entries are qualitatively better signals (they represent deliberate human choices) but are treated identically to non-refined entries in the current implementation — a weighting mechanism would improve precision
- The 5-entry cap on `DocumentBankService` means rare template types (e.g., governance-adr) accumulate examples very slowly
- No purge mechanism: if early low-quality documents fill the bank before quality improves, they occupy slots; the score-based eviction on overflow is the only remediation

### Failure Modes
- If disk is read-only or the example-bank directory cannot be created, all writes fail silently — the services log warnings and continue without training data
- A very long refined goal injected as few-shot context could push other important content out of the context window for shorter-context models
- The selection bank is per-templateType but not per-user — if two users have opposite preferences for the same template, their selections cancel out in the averages
