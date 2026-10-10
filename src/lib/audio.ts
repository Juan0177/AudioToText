const TARGET_SAMPLE_RATE = 16_000

function mergeChannels(buffer: AudioBuffer): Float32Array {
  const { numberOfChannels, length } = buffer
  if (numberOfChannels === 1) {
    return buffer.getChannelData(0).slice(0)
  }

  const mixed = new Float32Array(length)
  for (let channel = 0; channel < numberOfChannels; channel += 1) {
    const data = buffer.getChannelData(channel)
    for (let i = 0; i < length; i += 1) {
      mixed[i] += data[i] / numberOfChannels
    }
  }
  return mixed
}

function resample(input: Float32Array, fromRate: number, toRate: number): Float32Array {
  if (fromRate === toRate) return input

  const ratio = fromRate / toRate
  const newLength = Math.max(1, Math.round(input.length / ratio))
  const output = new Float32Array(newLength)

  for (let i = 0; i < newLength; i += 1) {
    const position = i * ratio
    const left = Math.floor(position)
    const right = Math.min(left + 1, input.length - 1)
    const fraction = position - left
    output[i] = input[left] * (1 - fraction) + input[right] * fraction
  }

  return output
}

export async function decodeAudioFile(file: Blob): Promise<Float32Array> {
  const arrayBuffer = await file.arrayBuffer()
  const audioContext = new AudioContext()

  try {
    const decoded = await audioContext.decodeAudioData(arrayBuffer.slice(0))
    const mono = mergeChannels(decoded)
    return resample(mono, decoded.sampleRate, TARGET_SAMPLE_RATE)
  } finally {
    await audioContext.close()
  }
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function formatDuration(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return '—'
  const mins = Math.floor(seconds / 60)
  const secs = Math.floor(seconds % 60)
  return `${mins}:${secs.toString().padStart(2, '0')}`
}

export const ACCEPTED_AUDIO =
  'audio/*,.ogg,.opus,.m4a,.mp3,.wav,.webm,.aac,.mp4,.mpeg,.mpga'
