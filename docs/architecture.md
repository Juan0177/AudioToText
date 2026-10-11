# AudioToText — Architecture

## Target (current)

ASP.NET Core 8 BFF + Bootstrap/jQuery UI with two production STT providers:

| Provider | Engine | When |
|----------|--------|------|
| **CloudflareRest** | Workers AI `@cf/openai/whisper` | Edge, no local model |
| **Local** | **Whisper.net** (whisper.cpp) | Privacy / offline; CUDA then CPU |
| Fake | Stub | Tests only |

### Why Whisper.net for Local (not Python sidecar)

- Stays in the preferred C# stack; NuGet runtimes for CPU + CUDA 12/13.
- `RuntimeOptions` probes CUDA → falls back to CPU without crashing.
- Model download via `WhisperGgmlDownloader` into gitignored `models/whisper/`.
- Python `faster-whisper` would be stronger on some GPUs but adds a second runtime/process; acceptable later if Whisper.net CUDA is insufficient on a given machine.

### Components

```
Browser (Bootstrap + jQuery)
  → POST /api/transcriptions
  → RoutingTranscriptionService (reads AppLocalSettingsStore.Provider)
       ├─ CloudflareWorkersAiTranscriptionService
       └─ LocalWhisperTranscriptionService
            ├─ AudioPcmConverter (ffmpeg preferred; NAudio for wav/mp3)
            └─ LocalWhisperRuntime (Whisper.net, model cache)
```

Settings (`/api/settings`): provider, local model, download, Cloudflare status-only secrets.
Persisted in `appsettings.Local.json` (gitignored).

### Hardware notes

| Machine | Advice |
|---------|--------|
| RTX 5070 | Local + `small` (CUDA); `medium` optional |
| GTX 1050 Ti 4GB | Local + `tiny`/`base`; expect CPU fallback if CUDA OOM |
| No GPU / no CUDA toolkit | Local + CPU; still fine for short WhatsApp notes |

### Legacy

`legacy/voce-browser/` — old in-browser transformers.js Whisper SPA.
