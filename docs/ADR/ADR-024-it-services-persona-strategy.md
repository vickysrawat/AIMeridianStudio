# ADR-024 — IT Services Consultant Persona Strategy for Research and Use Case Prompts

**Status:** Accepted  
**Date:** June 2026  
**Deciders:** MeridianStudio team

## Context

The original `BuildResearch` system prompt used a generic persona:

> *"You are an expert AI market research analyst and enterprise solutions architect."*

This produces market observations from an analyst's perspective — "the market for E-Discovery AI is large and growing…" — which is accurate but strategically shallow. The tool's audience is consultants at IT services firms (Capgemini-equivalent) who need to answer: *"Can we build and sell this as a service offering within 18 months?"*

That question requires a fundamentally different frame: the LLM must consider delivery feasibility, existing practice area alignment, SI partner ecosystem, and client relationships — not just market attractiveness.

## Decision

### Layered Persona Construction

Prompts are built with three additive layers rather than a single static string:

**Layer 1 — Base persona (always present):**
> *"You are a Senior AI Strategy Consultant at a Tier-1 IT services firm (equivalent to Capgemini, Infosys, or Wipro). You evaluate AI market opportunities from the perspective of what your firm can realistically build, sell, and deliver to enterprise clients within 12–24 months as a managed service or consulting engagement. Your scoring reflects not just market attractiveness but delivery feasibility from an IT services standpoint. Your analysis is used by practice leads and C-suite leadership to decide where to invest in new service line development."*

**Layer 2 — Domain specialisation (appended when domain is known):**

| Domain cluster | Appended expertise |
|---|---|
| Healthcare, Pharmaceutical | "8+ years delivering healthcare IT under HIPAA, HL7, FDA constraints" |
| Financial Services, Insurance | "Deep experience in PCI DSS, SOX, MiFID II regulated systems" |
| Law, Audit, Tax | "High compliance bar for legal/audit professional services technology" |
| IT Services, Telecom, Manufacturing | "AI/ML implementation specialist; assess technical maturity from a practitioner's view" |
| Government | "Procurement complexity and FedRAMP/NIST security requirements" |

**Layer 3 — Dimension-adaptive qualifiers (appended when weights cross thresholds):**

| Condition | Appended qualifier |
|---|---|
| `w_regulatory ≥ 18` | "Deep expertise in compliance and regulatory technology" |
| `w_ai_fitness ≥ 16` | "Specialise in AI/ML architecture and can assess model maturity and data readiness" |
| `w_competitive ≥ 18` | "Extensive knowledge of the vendor landscape and SI partner ecosystem" |
| `w_feasibility ≥ 20` | "Lead delivery teams; highly attuned to what can realistically be staffed and delivered" |

### Use Case Tab Persona

The Use Case tab uses a distinct persona tuned for feasibility assessment:

> *"You are a Senior Technical Architect at an IT consulting firm. A client has brought you this scenario and your firm will be responsible for implementing the recommended solution. You give honest, direct assessments — including uncomfortable truths about effort, risk, and what will actually work."*

The same domain-specialisation layer is applied after Groq extracts the domain from the free-text scenario.

### Implementation

`PromptBuilder.BuildResearchPersona(domain, weights)` assembles all three layers into a single system prompt string. It is called once per research request by `ResearchService` and passed into `BuildResearch(req, liveContext, persona)`. The persona string is not cached separately — it is cheap to construct and changes with each request's weight configuration.

## Consequences

### Positive
- Research output frames opportunities as service line investments, not market observations — directly actionable for practice leads.
- Domain specialisation eliminates generic disclaimers ("consult a healthcare IT specialist") because the model *is* positioned as that specialist.
- Dimension-adaptive qualifiers mean high-regulatory-weight analyses automatically adopt a compliance-aware voice without any user configuration.
- The Use Case persona's "honest, direct" framing reduces optimistic bias in feasibility scores.

### Negative / Trade-offs
- The base persona references specific firms ("Capgemini, Infosys, or Wipro") — if the tool is rebranded or sold to a different type of buyer, the persona must be updated.
- Layer 3 qualifiers stack with each other; a request with high urgency AND high AI fitness AND high competitive gap produces a very long persona string — token-inefficient but rarely triggered simultaneously.
- The LLM may ignore persona framing under heavy instruction load in longer prompts; the rubric anchors ([ADR-023](ADR-023-8-dimension-opportunity-scoring.md)) are the primary correctness mechanism.

### Failure Modes
- Domain detection returns wrong domain (e.g., "IT Services" for a healthcare use case) → wrong specialisation appended; persona frames the wrong expertise.
- LLM anchor-overrides persona framing in long prompts with conflicting system/user instructions.
