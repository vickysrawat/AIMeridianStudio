# Architecture Decision Records — MeridianStudio

This folder contains the Architecture Decision Records (ADRs) for MeridianStudio.
Each record documents a significant decision using the Nygard format: context → decision → consequences.

| # | Title | Status | Date |
|---|-------|--------|------|
| [ADR-001](ADR-001-multi-model-llm-cascade.md) | Multi-Model LLM Cascade | Accepted | June 2026 |
| [ADR-002](ADR-002-heuristic-engine-offline-fallback.md) | Heuristic Engine as Offline Fallback | Accepted | June 2026 |
| [ADR-003](ADR-003-sse-over-websocket.md) | SSE over WebSocket for Model Status | Accepted | June 2026 |
| [ADR-004](ADR-004-ddd-four-layer-backend.md) | DDD Four-Layer Backend Architecture | Accepted | June 2026 |
| [ADR-005](ADR-005-angular-signals-standalone.md) | Angular Signals + Standalone Components | Accepted | June 2026 |
| [ADR-006](ADR-006-json-only-llm-output.md) | JSON-Only LLM Output with Progressive Fallback Parsing | Accepted | June 2026 |
| [ADR-007](ADR-007-two-layer-response-caching.md) | Two-Layer Response Caching (Memory + Disk) | Accepted | June 2026 |
| [ADR-008](ADR-008-goal-directed-document-generation.md) | Goal-Directed Document Generation Loop | Accepted | June 2026 |
| [ADR-009](ADR-009-llm-generated-mission-suggestions.md) | LLM-Generated Mission Suggestions | Accepted | June 2026 |
| [ADR-010](ADR-010-two-store-self-training.md) | Two-Store Self-Training Architecture | Accepted | June 2026 |
| [ADR-011](ADR-011-keyword-solution-type-classifier.md) | Keyword-Heuristic Solution Type Classifier | Accepted | June 2026 |
| [ADR-012](ADR-012-persona-registry-static.md) | Persona Registry as Static; Tone/Goal/Criteria as Dynamic | Accepted | June 2026 |
| [ADR-013](ADR-013-gemini-sockets-http-handler.md) | Gemini HttpClient Uses SocketsHttpHandler | Accepted | June 2026 |
| [ADR-014](ADR-014-di-lifetime-strategy.md) | DI Lifetime Strategy: Singleton Infrastructure, Scoped Application | Accepted | June 2026 |
| [ADR-015](ADR-015-competitor-grounding-market-analysis.md) | Competitor Intelligence Grounding for Market Analysis Documents | Accepted | June 2026 |
| [ADR-016](ADR-016-tailwind-standalone-cli-build.md) | Tailwind Compiled Out-of-Band via Standalone CLI | Accepted | June 2026 |
| [ADR-017](ADR-017-html-light-theme-overrides.md) | Light/Dark Theming via `html.light` Class with Specificity-Layered Utility Overrides | Accepted | June 2026 |
| [ADR-018](ADR-018-blueprint-contract-documents-renderings.md) | Blueprint is the Architecture Contract; Documents are Renderings of It | Accepted | June 2026 |
| [ADR-019](ADR-019-blueprint-conversational-refinement.md) | Conversational Blueprint Refinement via Per-Panel Chat + Patch | Accepted | June 2026 |
| [ADR-020](ADR-020-domain-topologies-buy-build-regeneration.md) | Domain-Specific Topologies, Buy-vs-Build, and Explicit Topology Regeneration | Accepted | June 2026 |
| [ADR-021](ADR-021-use-case-driven-blueprint-feasibility.md) | Use-Case-Driven Blueprint with Feasibility Analysis | Accepted | June 2026 |
| [ADR-022](ADR-022-rag-web-search-enrichment.md) | RAG Web Search Enrichment Pipeline for Live Trend Intelligence | Accepted | June 2026 |
| [ADR-023](ADR-023-8-dimension-opportunity-scoring.md) | 8-Dimension Weighted Opportunity Scoring with Domain-Adaptive Profiles | Accepted | June 2026 |
| [ADR-024](ADR-024-it-services-persona-strategy.md) | IT Services Consultant Persona Strategy for Research and Use Case Prompts | Accepted | June 2026 |
| [ADR-025](ADR-025-structured-subdomain-research-workflow.md) | Structured Subdomain Research Workflow with 22-Domain Taxonomy | Accepted | June 2026 |
| [ADR-026](ADR-026-domain-adaptive-blueprint-generation.md) | Domain-Adaptive Blueprint Generation | Accepted | June 2026 |
| [ADR-027](ADR-027-trustworthy-structured-document-pipeline.md) | Trustworthy Structured-Native Document Pipeline | Accepted — impl. pending | June 2026 |
| [ADR-028](ADR-028-standalone-assessment-artifact.md) | Standalone Assessment Artifact for the Use Case Workflow | Accepted | June 2026 |
| [ADR-029](ADR-029-driven-white-paper-with-citation-scoping.md) | Research-Driven White Paper with Scoped Citations | Accepted | July 2026 |
| [ADR-030](ADR-030-lineage-anchored-grounding-critics.md) | Lineage-Anchored Chain: Server-Side Grounding, Durable Revisions, Advisory Critics | Accepted | July 2026 |

---

## How to Read These Records

- **Context** — the problem, constraints, or trigger that made a decision necessary
- **Decision** — what was decided and what it means specifically for the codebase
- **Consequences** — positive outcomes, trade-offs, and known failure modes

ADRs capture *why* — the code captures *what*. When the two diverge, update the ADR first.
