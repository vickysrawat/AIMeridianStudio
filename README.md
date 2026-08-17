# MeridianStudio

AI Solution Agent & System Architect Hub — generates prioritized AI solution ideas, system blueprints, technical documents, and developer handoff prompts for any industry vertical.

## Project Structure

```
AIMeridianStudio/
├── MeridianStudio.API/   ← .NET 10 Minimal API
└── MeridianStudio.UI/    ← Angular 19 frontend
```

## Tech Stack

| Layer | Technology |
|---|---|
| API | .NET 10, ASP.NET Core Minimal APIs, C# 13 |
| Frontend | Angular 19 (standalone components, signals), Tailwind CSS v4 |
| LLM Cascade | Gemini 2.5 Flash → Groq llama-3.3-70b → Claude Sonnet → Heuristic Engine (Offline) |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)

### Run the API

```bash
cd MeridianStudio.API
dotnet run --project MeridianStudio.API.csproj --launch-profile http
```

API: `http://localhost:5000`  
Scalar UI: `http://localhost:5000/scalar/v1`

### Run the UI

```bash
cd MeridianStudio.UI
npm install
npm start
```

UI: `http://localhost:4200`

### Configure LLM API Keys (optional)

The app works offline without keys — it falls back to the Heuristic Engine.

```bash
cd MeridianStudio.API
dotnet user-secrets set "LLM:Gemini:ApiKey" "AIza..."
dotnet user-secrets set "LLM:Groq:ApiKey"   "gsk_..."
dotnet user-secrets set "LLM:Claude:ApiKey" "sk-ant-..."
```

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/research` | Discover prioritized AI solution ideas for a domain |
| `POST` | `/api/generate-blueprint` | Generate a full system architecture blueprint |
| `POST` | `/api/execute-task` | Execute and simulate a development task |
| `POST` | `/api/generate-document` | Produce executive, technical, or proposal documents |
| `POST` | `/api/generate-component-prompt` | Create a developer handoff prompt for a component |
| `GET`  | `/api/health` | Health check |
| `GET`  | `/api/events/model-status` | SSE stream for real-time LLM routing events |

## Industry Verticals

The Heuristic Engine covers 9 verticals: Healthcare AI, Financial Technology, Legal Technology, Retail & E-Commerce, Real Estate & Property Management, Education & EdTech, Local Services, Core Software & Tech, and Enterprise AI Platform.

## LLM Model Routing

Each API response includes a `modelUsed` field indicating which provider handled the request:

| Value | Provider |
|---|---|
| `Gemini (gemini-2.5-flash)` | Google Gemini (primary) |
| `Groq (llama-3.3-70b-versatile)` | Groq (secondary) |
| `Claude (claude-sonnet-4-6)` | Anthropic Claude (tertiary) |
| `Heuristic Engine (Offline)` | Local fallback |

Real-time routing events are streamed via SSE at `/api/events/model-status`.
