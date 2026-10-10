import { useEffect, useRef, useState } from 'react'
import { DEFAULT_MODEL, type ModelId } from '../lib/models'

type WorkerProgress = {
  type: 'progress'
  message: string
  percent: number | null
}

type WorkerReady = { type: 'ready'; modelId: ModelId }
type WorkerResult = { type: 'result'; text: string }
type WorkerError = { type: 'error'; message: string }

type WorkerOutgoing = WorkerProgress | WorkerReady | WorkerResult | WorkerError

export type TranscriberStatus =
  | 'idle'
  | 'loading-model'
  | 'ready'
  | 'transcribing'
  | 'error'

export function useTranscriber() {
  const workerRef = useRef<Worker | null>(null)
  const [status, setStatus] = useState<TranscriberStatus>('idle')
  const [progressMessage, setProgressMessage] = useState('')
  const [progressPercent, setProgressPercent] = useState<number | null>(null)
  const [transcript, setTranscript] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [modelId, setModelId] = useState<ModelId>(DEFAULT_MODEL)
  const [readyModelId, setReadyModelId] = useState<ModelId | null>(null)

  useEffect(() => {
    const worker = new Worker(
      new URL('../workers/transcription.worker.ts', import.meta.url),
      { type: 'module' },
    )
    workerRef.current = worker

    worker.onmessage = (event: MessageEvent<WorkerOutgoing>) => {
      const data = event.data
      if (data.type === 'progress') {
        setProgressMessage(data.message)
        setProgressPercent(data.percent)
        return
      }
      if (data.type === 'ready') {
        setReadyModelId(data.modelId)
        setStatus('ready')
        setProgressMessage('Modello pronto')
        setProgressPercent(100)
        return
      }
      if (data.type === 'result') {
        setTranscript(data.text)
        setStatus('ready')
        setProgressMessage('Trascrizione completata')
        return
      }
      if (data.type === 'error') {
        setError(data.message)
        setStatus('error')
      }
    }

    worker.onerror = () => {
      setError('Il worker di trascrizione si è interrotto.')
      setStatus('error')
    }

    setStatus('loading-model')
    setProgressMessage('Avvio motore locale…')
    worker.postMessage({ type: 'load', modelId: DEFAULT_MODEL })

    return () => {
      worker.terminate()
      workerRef.current = null
    }
  }, [])

  const loadModel = (nextModelId: ModelId) => {
    if (!workerRef.current) return
    setModelId(nextModelId)
    setError(null)
    setStatus('loading-model')
    setProgressMessage('Cambio modello…')
    setProgressPercent(0)
    workerRef.current.postMessage({ type: 'load', modelId: nextModelId })
  }

  const transcribe = (audio: Float32Array, language: string | null) => {
    if (!workerRef.current) return
    if (status === 'loading-model' || status === 'transcribing') return

    setError(null)
    setTranscript('')
    setStatus('transcribing')
    setProgressMessage('Trascrizione in corso…')
    setProgressPercent(null)
    workerRef.current.postMessage({ type: 'transcribe', audio, language })
  }

  const clearTranscript = () => {
    setTranscript('')
    setError(null)
  }

  return {
    status,
    progressMessage,
    progressPercent,
    transcript,
    error,
    modelId,
    readyModelId,
    loadModel,
    transcribe,
    clearTranscript,
    setError,
  }
}
