import { useState } from 'react'

type TranscriptPanelProps = {
  text: string
  onClear: () => void
}

export function TranscriptPanel({ text, onClear }: TranscriptPanelProps) {
  const [copied, setCopied] = useState(false)

  if (!text) return null

  const copy = async () => {
    await navigator.clipboard.writeText(text)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 1600)
  }

  const download = () => {
    const blob = new Blob([text], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `voce-trascrizione-${Date.now()}.txt`
    anchor.click()
    URL.revokeObjectURL(url)
  }

  return (
    <section className="animate-rise mt-10 border-t border-mist/15 pt-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h2 className="font-display text-2xl font-bold text-foam">Trascrizione</h2>
          <p className="mt-1 text-sm text-muted">Resta sul dispositivo finché non la copi.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={copy}
            className="rounded-xl bg-honey px-4 py-2.5 text-sm font-semibold text-ink transition hover:brightness-105"
          >
            {copied ? 'Copiato' : 'Copia'}
          </button>
          <button
            type="button"
            onClick={download}
            className="rounded-xl border border-mist/30 px-4 py-2.5 text-sm font-semibold text-foam transition hover:border-mist/60"
          >
            Scarica .txt
          </button>
          <button
            type="button"
            onClick={onClear}
            className="rounded-xl px-4 py-2.5 text-sm font-semibold text-muted transition hover:text-foam"
          >
            Pulisci
          </button>
        </div>
      </div>
      <p className="mt-6 whitespace-pre-wrap text-base leading-relaxed text-foam/95 sm:text-lg">
        {text}
      </p>
    </section>
  )
}
