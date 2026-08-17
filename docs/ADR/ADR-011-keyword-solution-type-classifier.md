# ADR-011 — Keyword-Heuristic Solution Type Classifier

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

Mission suggestions (ADR-009) and document generation prompts need to be grounded in the **type of solution** being built — a REST API proposal needs different tone and criteria options than an Azure Serverless or event-driven solution. Without this grounding, suggestions are domain-specific (healthcare vs fintech) but architecturally generic.

Two approaches were considered:
1. **LLM-based classification:** Send the blueprint text to an LLM with a classification prompt → richer, more contextual detection
2. **Keyword heuristic:** Scan the blueprint text for known signal keywords → deterministic, instant, free

## Decision

`SolutionClassifierService` (`Application/Services/SolutionClassifierService.cs`) uses a **pure keyword heuristic** — no LLM call.

The service scans the concatenated `BaseTopology + CoreScenario + EndpointManifest` (lowercased) for 8 keyword groups:

| Solution Type | Signal Keywords |
|---|---|
| Azure Serverless | "function app", "azure function", "durable function", "consumption plan", etc. |
| Console App | "console app", "cli tool", "batch job", "worker service", etc. |
| Event-Driven | "event sourcing", "cqrs", "kafka", "dead letter", "saga pattern", etc. |
| Data Pipeline | "etl", "data lake", "databricks", "stream processing", etc. |
| ML Inference | "inference", "model serving", "onnx", "embedding", "mlops", etc. |
| Microservices | "service mesh", "kubernetes", "istio", "service discovery", etc. |
| Web App | "spa", "blazor", "react frontend", "mvc application", etc. |
| REST API | "rest api", "openapi", "swagger", "minimal api", etc. (default fallback) |

Confidence is calculated as `min(0.95, 0.40 + (matchedKeywords / totalKeywordsInGroup) * 0.55)`. The highest-confidence type wins.

Classification runs **immediately after blueprint generation** in `BlueprintService` and is stored on `SystemBlueprint.solutionType + solutionTypeConfidence`. Users can override via the Blueprinter UI badge (stored as `overrideSolutionType` in the next blueprint request or in `WorkspaceStoreService.solutionTypeOverride`).

## Consequences

### Positive
- Runs in < 1ms with zero network calls or external dependencies
- Deterministic — the same blueprint always produces the same type
- No LLM token cost for classification
- User override mechanism means an incorrect detection is a 2-click fix, not a blocker
- Confidence percentage shown in the UI badge gives users an immediate quality signal about the detection

### Negative / Trade-offs
- Keyword matching is shallow — it cannot understand architecture intent, only vocabulary. A blueprint that describes Kubernetes but is actually a monolith deployed on K8s would still classify as "Microservices"
- Short or sparse blueprints (especially from the Heuristic Engine) may not contain enough keyword signals — confidence will be low (near the 0.40 floor)
- "REST API" is the default fallback — any blueprint that doesn't strongly match another type will be classified as REST API, which is the most common type but may be incorrect
- No multi-type detection: a solution that is both event-driven AND deployed on Azure Serverless will be classified as whichever keyword group scores higher

### Failure Modes
- If the blueprint `CoreScenario` is in a non-English language or uses domain jargon that doesn't match the English keyword list, classification will fall through to the "REST API" default
- A future blueprint format change that moves topology information out of `BaseTopology + CoreScenario + EndpointManifest` would break detection — the field list is hardcoded
