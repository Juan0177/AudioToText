# AudioToText — Architecture

## Current (legacy: Voce)

| Layer | Choice | Notes |
|-------|--------|-------|
| UI | Vite + React 19 + Tailwind | Brand “Voce”; Italian copy |
| STT | `@huggingface/transformers` Whisper in a Web Worker | Models Tiny/Base/Small, WASM, browser cache |
| Hosting | Static (`dist`) → Pages/Vercel/Netlify | No backend |
| Privacy | Audio never leaves the device | Strength of the old design |
| Limits | Large first download; weak on phones; OGG decode flaky; no auth/history/ops | Not aligned with preferred stack |

Repo root previously held only this SPA (`src/`, `package.json`). Preserved under `legacy/voce-browser/`.

## Target

Production-leaning **ASP.NET Core 8** app (preferred stack) with Bootstrap/jQuery UI. Speech-to-text via **Cloudflare Workers AI Whisper** (edge inference), not in-browser WASM.

### Why edge AI instead of only ASP.NET hosting for the model

| Option | Verdict |
|--------|---------|
| Keep browser Whisper | Rejected for primary path: model download, device CPU, format pain; React/Tailwind ≠ preferred FE |
| Run Whisper inside ASP.NET (Whisper.net / Python sidecar) | Possible later; heavy native deps on Windows servers; harder to operate |
| **Cloudflare Workers AI Whisper** | Chosen: managed ASR, no model packaging, MCP-ready account already linked; ASP.NET remains the BFF/UI host |
| Pure Worker + Pages (no .NET) | Fits CF well but drops preferred C# backend |

**Decision:** ASP.NET Core owns UX, validation, and secrets. Transcription is an injectable `ITranscriptionService` backed by Workers AI (REST from the app, or optional Worker with `AI` binding).

### Components

```
Browser (Bootstrap 5 + jQuery)
  │  multipart upload / mic blob
  ▼
AudioToText.Web (ASP.NET Core 8)
  ├── wwwroot UI
  ├── POST /api/transcriptions  (validate size/type/language)
  └── ITranscriptionService
        ├── CloudflareWorkersAiTranscriptionService  (REST → /accounts/{id}/ai/run/{model})
        └── (optional) CloudflareWorkerProxyTranscriptionService
              └── Worker audiototext-transcribe (AI binding → Whisper)
```

- **Model (default):** `@cf/openai/whisper` (multilingual; Italian WhatsApp notes). Configurable to `@cf/openai/whisper-large-v3-turbo`.
- **Persistence:** none in v1 (stateless request/response). EF Core + SQL Server/SQLite reserved for future job history/users.
- **Auth:** none in v1; API key / Identity can wrap `/api/transcriptions` later.

### Data flow

1. User drops audio or records from mic.
2. Client `FormData` → `POST /api/transcriptions` (`file`, optional `language`).
3. Server validates content type, extension allow-list, max size (default 25 MB).
4. Service sends audio to Workers AI; returns `{ text, language?, wordCount? }`.
5. Client shows text; copy / download `.txt`. Audio is not stored after the request.

### Configuration

| Setting | Purpose |
|---------|---------|
| `Cloudflare:AccountId` | CF account id |
| `Cloudflare:ApiToken` | Token with Workers AI run permission (user secret; never commit) |
| `Cloudflare:Model` | e.g. `@cf/openai/whisper` |
| `Transcription:MaxUploadBytes` | Upload cap |
| `Transcription:Provider` | `CloudflareRest` (default) |

Local secrets: `dotnet user-secrets` or env vars `Cloudflare__AccountId`, `Cloudflare__ApiToken`.

### Deploy

| Surface | How |
|---------|-----|
| **Local Windows** | `dotnet run --project src/AudioToText.Web` → https://localhost:5xxx |
| **ASP.NET host** | IIS / Kestrel / Azure App Service; set Cloudflare secrets |
| **Optional CF Worker** | `cloudflare/transcribe-worker` — edge STT with AI binding; use when ASP.NET should not hold a CF API token |
| **Static-only path** | Not primary; legacy SPA remains under `legacy/` if privacy-local STT is needed |

### Security

- Validate uploads (extension + content-type + size).
- No SQL in v1 (injection N/A); future EF uses parameterized queries.
- CF token only in server config / user-secrets / host env.
- Clear errors to client; detailed failures logged server-side.

### Migration from Voce

| Keep | Drop / replace |
|------|----------------|
| Product goal: WhatsApp voice → text | React/Tailwind SPA as primary |
| Italian-first UX | In-browser model download UX |
| Copy/download transcript | transformers.js worker pipeline |

Legacy code kept for reference; not built by default.
