# ADR-006 — JSON-Only LLM Output with Progressive Fallback Parsing

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

LLM providers do not reliably produce machine-parseable output. Even when instructed to return JSON, providers frequently:
- Wrap the JSON in Markdown code fences (` ```json ... ``` `)
- Emit literal newline characters inside JSON string values instead of `\n`
- Include explanatory text before or after the JSON object
- Return fields as strings instead of numbers, or omit optional fields entirely
- Use unescaped double-quote characters inside string values

Without a deliberate parsing strategy, any of these behaviours causes a `JsonException` that bubbles up as a 500 error to the user.

## Decision

**Every LLM prompt ends with `JsonOnlyRule`:**
```
STRICT RULE: Respond with ONLY valid JSON.
Do NOT include markdown, code fences, commentary, or any text outside the JSON object.
Your entire response must start with { and end with }.
```

**Every provider response is parsed by `LLMResponseParser` using a three-attempt progressive fallback:**

1. **Attempt 1 — Clean parse:** Strip Markdown code fences, extract outermost `{ ... }`, parse with `JsonDocument` (trailing commas and comments allowed)
2. **Attempt 2 — Control escape:** If Attempt 1 fails, run `EscapeControlsInStrings()` which character-scans the JSON and replaces literal `\n`/`\r`/`\t` inside string values with `\\n`/`\\r`/`\\t`, then re-parse
3. **Attempt 3 — Raw fallback:** Try parsing the full raw response as-is

If all three attempts fail, `JsonException` is thrown, which causes the orchestrator to rotate to the next provider.

**After parsing, missing fields are filled with typed defaults** rather than throwing — e.g., `id` falls back to a SHA-256 hash of the request parameters, `status` defaults to `"Completed"`, numeric fields default to their semantic values (100 for progress score).

For the `content` field of `CorporateDocument` specifically, a **boundary extraction** strategy is used as an additional fallback: it locates the opening quote of `"content":` and the last `"` before the final `}` by character position rather than by JSON parsing — immune to unescaped double-quotes inside the document body.

## Consequences

### Positive
- The application never surfaces a raw `JsonException` to the user from a malformed LLM response
- The boundary extraction technique handles the single most common LLM formatting failure (unescaped quotes in long documents)
- The three-attempt strategy catches > 99% of real-world LLM formatting issues observed in development
- Default field values mean a partial LLM response still produces a usable result

### Negative / Trade-offs
- Defensive parsing adds ~15ms latency per response (string allocation + scanning)
- The `JsonOnlyRule` does not prevent all preamble — some providers (notably Gemini in conversational mode) still prepend a sentence before the JSON; the `ExtractJson` method handles this by finding the first `{`
- Overly generous fallback parsing can mask prompt engineering regressions — a response that required Attempt 3 should be investigated even if it "worked"

### Failure Modes
- The boundary extraction for `content` assumes the content value is the last field in the JSON object. If a provider reorders fields and places `content` before other fields, the boundary extraction will capture too much. Mitigated by the explicit field ordering in the prompt schema.
- `EscapeControlsInStrings` modifies the JSON string before re-parsing — if the original had intentional literal characters in a non-string position, the modification could produce invalid JSON. In practice this has not occurred.
