import { useEffect, useRef, useState } from 'react'
import { formatDuration } from '../lib/audio'

type RecorderProps = {
  disabled?: boolean
  onRecording: (blob: Blob) => void
}

export function Recorder({ disabled, onRecording }: RecorderProps) {
  const mediaRecorderRef = useRef<MediaRecorder | null>(null)
  const chunksRef = useRef<Blob[]>([])
  const timerRef = useRef<number | null>(null)
  const [recording, setRecording] = useState(false)
  const [seconds, setSeconds] = useState(0)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    return () => {
      if (timerRef.current) window.clearInterval(timerRef.current)
      mediaRecorderRef.current?.stream.getTracks().forEach((track) => track.stop())
    }
  }, [])

  const stop = () => {
    mediaRecorderRef.current?.stop()
  }

  const start = async () => {
    setError(null)
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
      const mimeType = MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
        ? 'audio/webm;codecs=opus'
        : MediaRecorder.isTypeSupported('audio/webm')
          ? 'audio/webm'
          : undefined

      const recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined)
      chunksRef.current = []
      mediaRecorderRef.current = recorder

      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) chunksRef.current.push(event.data)
      }

      recorder.onstop = () => {
        stream.getTracks().forEach((track) => track.stop())
        if (timerRef.current) window.clearInterval(timerRef.current)
        setRecording(false)
        const blob = new Blob(chunksRef.current, {
          type: recorder.mimeType || 'audio/webm',
        })
        setSeconds(0)
        onRecording(blob)
      }

      recorder.start()
      setRecording(true)
      setSeconds(0)
      timerRef.current = window.setInterval(() => {
        setSeconds((value) => value + 1)
      }, 1000)
    } catch {
      setError('Microfono non disponibile. Controlla i permessi del browser.')
    }
  }

  return (
    <div className="flex flex-col items-center gap-3">
      <button
        type="button"
        disabled={disabled}
        onClick={recording ? stop : start}
        className={`inline-flex min-h-12 items-center justify-center rounded-xl px-5 text-sm font-semibold transition ${
          recording
            ? 'bg-honey text-ink hover:brightness-105'
            : 'bg-mist text-ink hover:brightness-105'
        } disabled:cursor-not-allowed disabled:opacity-50`}
      >
        {recording ? `Ferma · ${formatDuration(seconds)}` : 'Registra dal microfono'}
      </button>
      {error ? <p className="text-sm text-honey">{error}</p> : null}
    </div>
  )
}
