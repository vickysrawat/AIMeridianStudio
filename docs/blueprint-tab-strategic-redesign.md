# Blueprint Tab — Strategic Redesign

**Date:** June 2026
**Status:** Proposed — pending implementation

---

## Context

The question that prompted this: *"We are already creating documents of different types, so what value add is Blueprint providing?"*

This is the right question. Code inspection reveals a real problem: **Blueprint and Documents are generating the same content independently**. The Technical Specification, Developer Handbook, and Detailed Design documents all re-generate SQL schemas, REST endpoint tables, and architecture descriptions from scratch — the same things the blueprint already produced. Documents receive only 1,500 characters of `coreScenario` prose (hard-truncated in `InputGuard`) and ignore the blueprint's SQL DDL, endpoint manifest, and resilience strategies entirely. The blueprint's structured technical content is thrown away at the document API boundary.

The result: the blueprint looks like five documents. Documents look like a second set of five documents covering the same ground. Users have no clear mental model for when to use which.

---

## The Core Problem (Precise Technical Statement)

In `DocumentService.GenerateGoalDirectedAsync` → `PromptBuilder.BuildDocument`:

```
blueprintContext = req.BlueprintContext  // InputGuard.Sanitize(..., 1500 chars of coreScenario)
```

That is the **only** blueprint data documents receive. The `databaseSchemes`, `endpointManifest`, `resilienceStrategies`, and `solutionType` from the blueprint are never read by the document generator. So when a Technical Specification document says "here is the database schema", it invented that schema from the 1,500-char narrative — not from the blueprint's actual DDL. They diverge silently.

---

## New Value Proposition

> **Blueprint = the architecture contract (structured, machine-readable decisions). Documents = audience-shaped renderings of that contract.**

| | Blueprint | Documents |
|---|---|---|
| Answers | *What is the system?* | *What does the system mean to you?* |
| Content | Technologies, quality targets, decisions made and why | Stakeholder-specific narratives |
| Authority | Source of truth | Derived view |

This maps to the OpenAPI analogy: the spec is the contract; Swagger UI, client SDKs, and mocks are renderings. Currently MeridianStudio generates five Swagger UIs that each independently describe a different API.

---

## What Changes

### Phase 1 — Fix the data pipeline ✦ Highest impact (1–2 days)

**Problem:** Documents truncate blueprint context to 1,500 chars and ignore structured fields.

**Fix:** In `DocumentService`, retrieve the cached `SystemBlueprint` by `blueprintId` from `PayloadCache` and pass its full structured fields into the document prompt.

**Files to change:**
- `Application/Services/DocumentService.cs` — call `PayloadCache.TryGet<SystemBlueprint>(blueprintId, out var bp)` at the top of `GenerateGoalDirectedAsync`; pass `bp` to the prompt builders
- `Infrastructure/LLM/PromptBuilder.cs` — `BuildDocument` / `BuildDocumentPatch` — replace the 1,500-char `contextSection` with a structured embed:

```
BLUEPRINT CONTRACT (embed verbatim — do NOT regenerate):
Architecture: {bp.SolutionType}
Domain: {bp.Domain}

Technical narrative:
{bp.CoreScenario}   ← full text, no truncation

Endpoint surface:
{bp.EndpointManifest}   ← embed the actual table

Data stores:
{bp.DatabaseSchemes}   ← embed the actual DDL summary
```

Template-specific instructions for `technical-specification` and `detailed-design` should add: *"Embed the technology and schema data from BLUEPRINT CONTRACT above verbatim. Do not invent alternatives."*

**Result:** Documents stop diverging from the blueprint. Technical Specification embeds the real endpoint table. Detailed Design references the real schemas.

---

### Phase 2 — Add the WHY layer (2–3 days) ✦ True differentiation

The blueprint currently captures *what* (topology, schemas, endpoints). It never captures *why* (why PostgreSQL over MongoDB, why REST over GraphQL, why circuit breaker at 50% failure threshold). This is the layer only the blueprint can provide — documents cannot generate it because the reasons are architectural decisions, not audience renderings.

**Add two new panels to `SystemBlueprint`:**

**A. Architecture Decision Log** — replaces the prose `resilienceStrategies` wall

New model fields (list of typed records):
```csharp
public sealed record ArchDecision(
    string Decision,
    string ChosenApproach,
    string Rationale,
    string[] AlternativesConsidered,
    string[] Risks
);
```
The LLM is asked to produce 4–6 decisions: data store choice, service decomposition strategy, API style, auth approach, resilience pattern, AI/ML integration pattern.

The `governance-adr` document type currently generates ADRs from scratch, disconnected from the blueprint. With this change, it renders from the blueprint's decisions instead.

**B. Quality Attribute Scorecard** — replaces the NFRs buried in `coreScenario` prose

New model fields (list of typed records):
```csharp
public sealed record QualityAttribute(
    string Attribute,   // "Availability"
    string Target,      // "99.95%"
    string Measurement  // "uptime over 30-day rolling window"
);
```
5–8 rows: Availability, Response Time, Throughput, Data Retention, Security, Compliance, Recovery Time Objective.

Documents embed the exact metric values instead of fabricating percentages.

**Files to change:**
- `Domain/Models/SystemBlueprint.cs` — add `ArchDecisions` and `QualityAttributes` list fields
- `Infrastructure/LLM/PromptBuilder.cs` — `BuildBlueprint`: update JSON schema to request structured fields; de-emphasize full SQL DDL and ASCII art (request summaries)
- `Infrastructure/LLM/LLMResponseParser.cs` — `ParseBlueprint`: deserialize new fields
- `Infrastructure/LocalEngine/LocalCompilationEngine.cs` — populate from domain profiles
- `MeridianStudio.UI/src/app/core/models/interfaces.ts` — add `archDecisions` and `qualityAttributes` to `SystemBlueprint` interface

---

### Phase 3 — Refocus the Blueprint UI (1–2 days)

The current 5 panels show implementation artifacts (SQL DDL, ASCII art, endpoint tables). These belong in documents, not in an EA-level blueprint view.

**New panel layout:**

| # | Panel | What it shows | Replaces |
|---|---|---|---|
| 01 | Core Scenario | 150-word executive summary of the system | Current 300-word wall |
| 02 | Solution Type + Cloud Target | Badge + detected cloud platform | Solution type badge (keep) |
| 03 | Architecture Decisions | ADR-lite table: Decision / Approach / Why not alternatives / Risks | Resilience strategies prose |
| 04 | Quality Attribute Scorecard | Table: Attribute / Target / Measurement | NFRs buried in coreScenario |
| 05 | Technology Radar | Dot grid by layer (Frontend/Backend/Data/Infra/AI) | BaseTopology ASCII art |
| 06 | Implementation Detail (collapsed accordion) | SQL DDL, endpoint manifest, ASCII topology | Current panels 02–04 |

The SQL DDL and endpoint tables move to a collapsible accordion — not removed, but demoted from primary architecture panels to implementation detail.

**File to change:**
- `MeridianStudio.UI/src/app/features/architectural-blueprinter/architectural-blueprinter.component.ts`

---

## What NOT to Build

| Idea | Reason to skip |
|---|---|
| C4 interactive diagrams | 3–6 week frontend effort; not justified now |
| Mermaid/PlantUML rendering | ASCII stays in accordion as-is |
| Blueprint versioning / diff tracking | No downstream consumer of multiple versions |
| Multi-blueprint portfolio view | Valuable eventually, out of scope |
| Import from ArchiMate or draw.io | Inverts the value proposition |

---

## How Documents Change (Role Clarification)

No document types need to be removed. Their role simply becomes clearer:

| Document | Role after this change |
|---|---|
| Technical Specification | Embeds blueprint endpoint manifest + schemas; adds engineering detail |
| Detailed Design | Embeds blueprint data stores; adds sprint breakdown |
| Governance & ADR | Renders blueprint's `ArchDecisions` for a compliance/audit audience |
| Executive Summary | Embeds blueprint's `QualityAttributes` targets; stakeholder narrative |
| Market Analysis | Unchanged — grounded in research competitor data |
| Developer Handbook | Embeds blueprint tech radar; adds onboarding narrative |
| Proposal | Unchanged — commercial framing of the solution |

---

## Verification Checklist

1. Generate a blueprint for a Healthcare AI solution
2. Generate a Technical Specification document
3. **Check:** endpoint table in the document matches the blueprint's `EndpointManifest` — identical (embedded), not independently generated
4. **Check:** quality attribute targets in the Executive Summary match the blueprint's `QualityAttributes` exact figures
5. **Check:** Governance ADR document renders the blueprint's `ArchDecisions` with compliance framing — does not invent new decisions
6. **Check:** Blueprint UI shows the ADR table and QA scorecard panels; SQL DDL is in a collapsed accordion
