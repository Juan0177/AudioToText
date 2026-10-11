# AudioToText

Trascrizione audio (vocali WhatsApp) con **ASP.NET Core 8**:

- **Cloudflare** Workers AI Whisper, oppure
- **Locale** Whisper.net (CUDA se disponibile, altrimenti CPU)

UI: Bootstrap 5 + jQuery.

## Architettura

Vedi [`docs/architecture.md`](docs/architecture.md).

## Requisiti (Windows)

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Locale / WhatsApp ogg·opus:** [ffmpeg](https://ffmpeg.org/download.html) nel `PATH` (o `FFMPEG_PATH`)
- **Locale / GPU:** driver NVIDIA + CUDA (Whisper.net.Runtime.Cuda). Senza CUDA → CPU automatico
- **Cloudflare:** Account ID + API Token Workers AI (opzionale se usi solo Locale)

## Avvio

```powershell
cd D:\dev\AudioToText
.\run-local.ps1
```

Apri http://localhost:5088 → **Impostazioni**.

### Modalità Locale

1. Provider → **Locale (Whisper)** → Salva provider  
2. Scegli modello (`tiny` / `base` / `small` / `medium`) → Salva modello  
3. **Scarica modello** (prima volta; file in `src/AudioToText.Web/models/whisper/`, gitignored)  
4. Torna a **Trascrivi**

Hint hardware:

| GPU | Consiglio |
|-----|-----------|
| GTX 1050 Ti (~4GB) | `tiny` o `base` (CPU ok se CUDA stretta) |
| RTX 5070 | `small` su CUDA; `medium` se VRAM basta |

### Modalità Cloudflare

In Impostazioni inserisci Account ID + API Token (mascherati; mai riletti dall’API). Salvati in `appsettings.Local.json` (gitignored).

## Test

```powershell
dotnet test
```

## Note

- Formati: ogg, opus, m4a, mp3, wav, webm, … (ogg/opus → ffmpeg)
- Max upload default: 25 MB
- Modelli Whisper **non** vanno in git
