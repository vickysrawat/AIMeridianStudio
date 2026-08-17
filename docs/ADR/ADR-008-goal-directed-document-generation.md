# ADR-008 — Goal-Directed Document Generation Loop

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

The original document generation flow was: one LLM call → return result. This produced documents that were structurally correct but often generic — they did not serve any particular reader's actual decision-making needs. A "Proposal" document looked the same regardless of whether it was for a risk-averse compliance officer or an ROI-focused CFO. There was no mechanism to verify whether the document had actually achieved its purpose.

The root problem: generation was **template-first** (what sections should this document have?) instead of **goal-first** (what does the reader need to be able to decide?).

## Decision

Document generation in `DocumentService` runs a **goal-directed iteration loop** with a maximum of 5 passes (`MaxIterations = 5`):

```
for i in 1..5:
    if i == 1: full generation prompt (BuildDocument)
    else:      patch prompt (BuildDocumentPatch) — only failed criteria addressed
    evaluate against user's selected criteria → GoalEvaluation
    if GoalAchieved (GoalAchievementPct >= 65%): break
    gaps = failedCriteria + failureReasons  ← injected into next patch call
```

**Goal achievement threshold: 65%** (set in `DocumentGoalJudgeService`) — a document that satisfies 65% or more of the user's selected criteria is considered "goal achieved". This threshold was deliberately set below 80% to avoid infinite loops on subjective criteria and because the judge's binary pass/fail per criterion is coarser than a continuous score.

**Pass 1 uses `BuildDocument`** — full generation with persona, examples from the document bank, and the selected goal/criteria.

**Passes 2–5 use `BuildDocumentPatch`** — the same persona and goal, but instead of full regeneration the LLM receives:
- `previousContent` — the full text of the previous pass to avoid losing content that already passed
- `gaps` — the specific criteria names that failed
- `gapReasons` — the judge's per-criterion failure explanation (why each criterion was not met)

This is targeted self-correction, not a full regeneration from scratch. The LLM edits the prior document to address only the identified gaps.

**`DocumentGoalJudgeService`** makes the evaluation call using a dedicated LLM judge prompt. The judge is prompted to take the **stakeholder's perspective** — not a generic quality score, but "would this document help the reader make their decision?"

The document's `iterationsUsed`, `goalAchievementPct`, `goalAchieved`, `passedCriteria`, and `failedCriteria` fields are returned to the client, making the generation process transparent.

**Heuristic Engine documents bypass the judge** — evaluation would be meaningless for deterministic static output. They are returned immediately with `GoalAchieved: true` and `GoalAchievementPct: 85`.

## Consequences

### Positive
- Documents are measurably better on first delivery — the judge provides a signal that prevents low-quality outputs from reaching the user unchanged
- The iteration mechanism is self-correcting without manual intervention
- Transparency: users see exactly which criteria passed and failed, and how many passes were needed
- Failed criteria are clickable in the UI — users can refine specific criteria and regenerate, closing the feedback loop

### Negative / Trade-offs
- Up to 5 LLM calls per document instead of 1 — latency can reach 60–90 seconds for a full 5-pass run (rare in practice; most documents achieve the goal in 1–2 passes)
- The judge call itself is an LLM call — it adds latency and cost even when the first pass achieves the goal
- 65% threshold is a heuristic — there is no mathematical basis for this specific value; it may need adjustment per template type. The current value was chosen to prevent the loop running to the maximum on borderline quality documents.
- The judge evaluates content against criteria text literally — a criterion like "Does it mention ROI?" will pass even if the ROI mention is one sentence in a 1,000-word document

### Failure Modes
- If the judge consistently scores below 65% regardless of content quality (judge miscalibration), the system always runs 5 passes — adding latency with diminishing returns. Monitoring pass count distributions can detect this.
- If `DocumentGoalJudgeService` throws, it falls back to `PassAll()` — the document is returned as-is rather than blocking the user. This is the correct trade-off for a demo application.
- Patch prompts on passes 2–5 include both `previousContent` (full prior document) and gap descriptions — for long documents this can be 2,000–4,000 tokens of context overhead per iteration, which could approach context window limits on shorter-context models (e.g. Groq llama-3.3-70b at 8k output).
