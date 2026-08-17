# ADR-003 — SSE over WebSocket for Real-Time Model Status

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

MeridianStudio users needed real-time visibility into which LLM provider is processing their request — particularly to understand when a cascade failover is occurring (e.g., Gemini quota exceeded → rotating to Groq). This requires a persistent push channel from the API to the browser.

The two standard mechanisms are WebSocket (bidirectional) and Server-Sent Events (SSE, server-to-client only). A third option — HTTP polling — was rejected immediately as too high-latency and wasteful.

## Decision

**SSE (`text/event-stream`)** was chosen over WebSocket for the model status stream at `GET /api/events/model-status`.

Rationale:
- Model status is **unidirectional** — the browser only receives events, never sends them. WebSocket's bidirectional capability is unused overhead.
- SSE works over **standard HTTP/1.1** — no protocol upgrade handshake, no proxy configuration issues, compatible with all infrastructure the app might be deployed behind.
- The browser-native **`EventSource` API** handles reconnection automatically — no client-side reconnect logic needed.
- A **heartbeat comment** (`": heartbeat"`) is emitted every 25 seconds to prevent proxy timeout disconnects.
- The Angular UI connects once on app initialisation (`WorkspaceStoreService._setupModelStatusStream()`) and reconnects with a 5-second delay on error.

Events carry a typed payload: `{ type, provider, operation, timestamp }` where `type` is one of `connected | attempting | succeeded | failed | fallback`.

## Consequences

### Positive
- Simpler server implementation than WebSocket — standard `HttpResponse` streaming, no hub infrastructure
- No additional NuGet packages required (pure ASP.NET Core)
- Automatic browser reconnect via `EventSource.onerror` handler
- Works through most corporate proxies without special configuration
- Easy to test with `curl -N http://localhost:5000/api/events/model-status`

### Negative / Trade-offs
- SSE is HTTP/1.1 only — HTTP/2 and HTTP/3 have their own multiplexed push mechanisms; SSE may be superseded in future infrastructure upgrades
- Limited to text data — binary payloads would require base64 encoding
- No browser-side acknowledgement — if the client misses an event during a brief disconnect, it does not receive a replay
- Maximum concurrent SSE connections per browser domain is capped (6 for HTTP/1.1) — not a problem for a single-tab app, but would require multiplexing in a multi-tab scenario

### Failure Modes
- If the API server restarts, all SSE clients disconnect. The 5-second reconnect loop in `WorkspaceStoreService` recovers silently.
- Proxies that buffer responses (e.g., nginx without `X-Accel-Buffering: no`) will suppress events until the buffer fills — the heartbeat mitigates this but does not eliminate it entirely.
