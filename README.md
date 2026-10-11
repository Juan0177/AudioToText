# AudioToText

Trascrizione audio (vocali WhatsApp) con **ASP.NET Core 8** + **Cloudflare Workers AI Whisper**.

UI: Bootstrap 5 + jQuery. Backend valida l’upload e chiama Workers AI.

## Architettura

Vedi [`docs/architecture.md`](docs/architecture.md).

- `src/AudioToText.Web` — app host + API `POST /api/transcriptions`
- `cloudflare/transcribe-worker` — Worker opzionale con binding `AI`
- `legacy/voce-browser` — vecchia SPA Whisper-in-browser

## Requisiti (Windows)

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Account Cloudflare + API token con permesso **Workers AI** (produzione)

## Avvio locale

Shortcut: `.\run-local.ps1` (checkout `main`, pull, then `dotnet run`).

```powershell
cd D:\dev\AudioToText
dotnet restore
dotnet run --project src\AudioToText.Web
```

Apri http://localhost:5088 (profilo `http`).

In **Development** il provider è `Fake` (`appsettings.Development.json`): l’UI funziona senza token.

### Cloudflare (produzione / STT reale)

```powershell
dotnet user-secrets set "Cloudflare:AccountId" "<account_id>" --project src\AudioToText.Web
dotnet user-secrets set "Cloudflare:ApiToken" "<api_token>" --project src\AudioToText.Web
```

Poi in `appsettings.Development.json` (o env):

```json
"Transcription": { "Provider": "CloudflareRest" }
```

Env vars equivalenti: `Cloudflare__AccountId`, `Cloudflare__ApiToken`, `Transcription__Provider`.

### Worker edge (opzionale)

```powershell
cd cloudflare\transcribe-worker
npm install
npx wrangler deploy
```

## Test

```powershell
dotnet test
```

## Note

- Formati: ogg, opus, m4a, mp3, wav, webm, …
- Max upload default: 25 MB
- Modello default: `@cf/openai/whisper`
