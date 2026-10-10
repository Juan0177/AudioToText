import { useMemo, useState } from 'react'
import { DropZone } from './components/DropZone'
import { Recorder } from './components/Recorder'
import { TranscriptPanel } from './components/TranscriptPanel'
import { Waveform } from './components/Waveform'
import { useTranscriber } from './hooks/useTranscriber'
import { decodeAudioFile, formatBytes } from './lib/audio'
import { MODEL_OPTIONS } from './lib/models'

type LanguageMode = 'italian' | 'auto'

export default function App() {
  const {
    status,
    progressMessage,
    progressPercent,
    transcript,
    error,
    modelId,
    loadModel,
    transcribe,
    clearTranscript,
    setError,
  } = useTranscriber()

  const [language, setLanguage] = useState<LanguageMode>('italian')
  const [fileLabel, setFileLabel] = useState<string | null>(null)
  const [busyLocal, setBusyLocal] = useState(false)

  const busy = busyLocal || status === 'loading-model' || status === 'transcribing'
  const engineReady = status === 'ready' || status === 'transcribing' || Boolean(transcript)

  const statusLine = useMemo(() => {
    if (error) return error
    if (status === 'loading-model') {
      return progressPercent != null
        ? `${progressMessage} (${progressPercent}%)`
        : progressMessage
    }
    if (status === 'transcribing') return progressMessage || 'Trascrizione in corso…'
    if (engineReady) return 'Motore locale pronto. L’audio non lascia questo dispositivo.'
    return 'Preparazione motore…'
  }, [engineReady, error, progressMessage, progressPercent, status])

  const runTranscription = async (blob: Blob, label: string) => {
    setBusyLocal(true)
    setError(null)
    setFileLabel(label)
    clearTranscript()

    try {
      const audio = await decodeAudioFile(blob)
      if (audio.length === 0) {
        throw new Error('File audio vuoto o non leggibile.')
      }
      transcribe(audio, language === 'italian' ? 'italian' : null)
    } catch (err) {
      const message =
        err instanceof Error
          ? err.message
          : 'Impossibile leggere questo audio. Prova un altro formato.'
      setError(
        message.includes('decode') || message.includes('Unable')
          ? 'Questo formato non è supportato dal browser. Esporta il vocale o convertilo in m4a/mp3/wav.'
          : message,
      )
    } finally {
      setBusyLocal(false)
    }
  }

  return (
    <div className="relative min-h-dvh overflow-hidden bg-ink">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_20%_0%,rgba(19,64,72,0.85),transparent_42%),radial-gradient(circle_at_80%_10%,rgba(232,184,109,0.12),transparent_35%),linear-gradient(180deg,#071316_0%,#0b1f24_48%,#071316_100%)]" />
      <Waveform active={status === 'transcribing' || status === 'loading-model'} />

      <main className="relative mx-auto flex min-h-dvh w-full max-w-3xl flex-col px-5 pb-16 pt-8 sm:px-8 sm:pt-12">
        <header className="animate-rise">
          <p className="font-display text-5xl font-extrabold tracking-tight text-foam sm:text-7xl">
            Voce
          </p>
          <h1 className="mt-4 max-w-xl text-xl font-medium leading-snug text-foam/90 sm:text-2xl">
            Trascrivi i vocali di WhatsApp sul tuo telefono o PC.
          </h1>
          <p className="mt-3 max-w-lg text-sm leading-relaxed text-muted sm:text-base">
            Whisper gira nel browser: niente bot a pagamento, niente upload su server
            terzi. Apri il link da ovunque e carica l’audio.
          </p>
        </header>

        <section className="animate-rise-delay-1 mt-10 space-y-5">
          <DropZone
            disabled={busy}
            onFile={(file) => {
              void runTranscription(file, `${file.name} · ${formatBytes(file.size)}`)
            }}
          />

          <div className="flex flex-col items-center gap-4 sm:flex-row sm:justify-between">
            <Recorder
              disabled={busy}
              onRecording={(blob) => {
                void runTranscription(blob, `Registrazione · ${formatBytes(blob.size)}`)
              }}
            />

            <div className="flex flex-wrap items-center justify-center gap-3 text-sm">
              <label className="flex items-center gap-2 text-muted">
                Lingua
                <select
                  value={language}
                  disabled={busy}
                  onChange={(event) => setLanguage(event.target.value as LanguageMode)}
                  className="rounded-lg border border-mist/25 bg-deep px-3 py-2 text-foam outline-none focus:border-honey"
                >
                  <option value="italian">Italiano</option>
                  <option value="auto">Rileva automatico</option>
                </select>
              </label>

              <label className="flex items-center gap-2 text-muted">
                Modello
                <select
                  value={modelId}
                  disabled={busy}
                  onChange={(event) =>
                    loadModel(event.target.value as (typeof MODEL_OPTIONS)[number]['id'])
                  }
                  className="rounded-lg border border-mist/25 bg-deep px-3 py-2 text-foam outline-none focus:border-honey"
                >
                  {MODEL_OPTIONS.map((option) => (
                    <option key={option.id} value={option.id}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </label>
            </div>
          </div>

          <p
            className={`text-center text-sm sm:text-left ${
              error ? 'text-honey' : 'text-muted'
            }`}
            role="status"
            aria-live="polite"
          >
            {statusLine}
            {fileLabel && !error ? (
              <span className="mt-1 block text-foam/70">{fileLabel}</span>
            ) : null}
          </p>

          {status === 'loading-model' && progressPercent != null ? (
            <div className="h-1.5 overflow-hidden rounded-full bg-teal/80">
              <div
                className="h-full rounded-full bg-gradient-to-r from-mist to-honey transition-[width] duration-300"
                style={{ width: `${Math.max(4, progressPercent)}%` }}
              />
            </div>
          ) : null}
        </section>

        <div className="animate-rise-delay-2">
          <TranscriptPanel text={transcript} onClear={clearTranscript} />
        </div>

        <footer className="mt-auto pt-12 text-xs leading-relaxed text-muted/80">
          <p>
            Dopo il primo download il modello resta in cache del browser. Per usarlo da
            tutte le parti: pubblica questa app (Cloudflare Pages, Vercel o Netlify) e
            apri l’URL sul telefono.
          </p>
        </footer>
      </main>
    </div>
  )
}
