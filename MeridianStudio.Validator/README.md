# MeridianStudio.Validator — browserless Mermaid validate-and-repair sidecar

A small, stateless Node service that **validates and deterministically repairs Mermaid diagrams**
(and validates whole documents) without a browser. Mermaid is JavaScript and the .NET API can't run
it, so this sidecar owns validation; the API calls it over HTTP and layers the tiered self-healing
(learned-fix cache → one-time LLM → cache) on top.

## Why no Chromium
Flowchart validation uses `mermaid.parse()` under **`jsdom`** — verified to return valid/invalid
correctly with no browser. `@mermaid-js/parser` covers only the newer langium grammars (not
flowcharts). Playwright/headless-Chromium is only needed for pixel-accurate *render* validation and is
intentionally **not** a dependency here (keeps the image ~150 MB lighter). Document validation parses
every fenced ```mermaid block via the same path.

## Run
```bash
npm install
npm start           # http://127.0.0.1:5177  (PORT / HOST env override)
npm test            # vitest: pure catalog + headless repair-loop integration
```

## Endpoints
| Method | Path | Body | Returns |
|---|---|---|---|
| GET  | `/healthz` | — | `{ status, service }` |
| POST | `/validate/diagram` | `{ source }` | `{ ok, error?, errorSignature? }` |
| POST | `/repair/diagram` | `{ source }` | `{ ok, repaired, rulesApplied[], error?, errorSignature? }` |
| POST | `/validate/document` | `{ markdown }` | `{ ok, diagrams[], issues[] }` |

## The rule catalog (`src/mermaid-fixes.ts`)
The single source of truth for deterministic fixes — pure `string → string` transforms, no deps.
`ALWAYS_ON` runs before the first validate; `REPAIR_CATALOG` runs only on parse failure. **Adding a
fix = one entry + one fixture** (see the header comment in that file). Repairs preserve information
(e.g. `trailing-edge-label` merges `|X — Y|` instead of dropping the protocol `Y`).
