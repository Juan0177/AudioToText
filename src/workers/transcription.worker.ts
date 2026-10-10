import { env, pipeline } from '@huggingface/transformers'
import type { ModelId } from '../lib/models'

env.allowLocalModels = false
env.useBrowserCache = true

type ProgressPayload = {
  status?: string
  file?: string
  progress?: number
  loaded?: number
  total?: number
}

type IncomingMessage =
  | { type: 'load'; modelId: ModelId }
  | {
      type: 'transcribe'
      audio: Float32Array
      language: string | null
    }

type OutgoingMessage =
  | { type: 'progress'; message: string; percent: number | null }
  | { type: 'ready'; modelId: ModelId }
  | { type: 'result'; text: string }
  | { type: 'error'; message: string }

type Transcriber = (
  audio: Float32Array,
  options: {
    language?: string
    task: 'transcribe'
    return_timestamps: false
  },
) => Promise<{ text: string } | string>

let transcriber: Transcriber | null = null
let loadedModelId: ModelId | null = null

function post(message: OutgoingMessage) {
  self.postMessage(message)
}

function progressLabel(data: ProgressPayload): { message: string; percent: number | null } {
  const percent =
    typeof data.progress === 'number'
      ? Math.round(data.progress)
      : data.loaded && data.total
        ? Math.round((data.loaded / data.total) * 100)
        : null

  if (data.status === 'initiate' || data.status === 'download') {
    return {
      message: data.file ? `Scarico ${data.file}…` : 'Scarico il modello…',
      percent,
    }
  }

  if (data.status === 'progress') {
    return {
      message: data.file ? `Scarico ${data.file}…` : 'Download in corso…',
      percent,
    }
  }

  if (data.status === 'done') {
    return { message: 'Modello pronto, preparazione…', percent: 100 }
  }

  if (data.status === 'ready') {
    return { message: 'Modello caricato', percent: 100 }
  }

  return { message: 'Caricamento modello…', percent }
}

async function loadModel(modelId: ModelId) {
  if (transcriber && loadedModelId === modelId) {
    post({ type: 'ready', modelId })
    return
  }

  post({ type: 'progress', message: 'Carico Whisper nel browser…', percent: 0 })

  transcriber = (await pipeline('automatic-speech-recognition', modelId, {
    dtype: 'q8',
    device: 'wasm',
    progress_callback: (data: ProgressPayload) => {
      const label = progressLabel(data)
      post({ type: 'progress', ...label })
    },
  })) as Transcriber

  loadedModelId = modelId
  post({ type: 'ready', modelId })
}

async function transcribe(audio: Float32Array, language: string | null) {
  if (!transcriber) {
    throw new Error('Modello non ancora caricato.')
  }

  post({ type: 'progress', message: 'Trascrizione in corso…', percent: null })

  const result = await transcriber(audio, {
    ...(language ? { language } : {}),
    task: 'transcribe',
    return_timestamps: false,
  })

  const text = (typeof result === 'string' ? result : result.text).trim()
  post({ type: 'result', text: text || '(Nessun parlato rilevato)' })
}

self.onmessage = async (event: MessageEvent<IncomingMessage>) => {
  try {
    const data = event.data
    if (data.type === 'load') {
      await loadModel(data.modelId)
      return
    }
    if (data.type === 'transcribe') {
      await transcribe(data.audio, data.language)
    }
  } catch (error) {
    const message =
      error instanceof Error ? error.message : 'Errore sconosciuto durante la trascrizione.'
    post({ type: 'error', message })
  }
}
