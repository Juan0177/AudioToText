export type ModelId =
  | 'onnx-community/whisper-tiny'
  | 'onnx-community/whisper-base'
  | 'onnx-community/whisper-small'

export type ModelOption = {
  id: ModelId
  label: string
  hint: string
}

export const MODEL_OPTIONS: ModelOption[] = [
  {
    id: 'onnx-community/whisper-tiny',
    label: 'Tiny',
    hint: 'Più veloce, ideale dal telefono',
  },
  {
    id: 'onnx-community/whisper-base',
    label: 'Base',
    hint: 'Buon equilibrio qualità/velocità',
  },
  {
    id: 'onnx-community/whisper-small',
    label: 'Small',
    hint: 'Più accurato, più lento al primo download',
  },
]

export const DEFAULT_MODEL: ModelId = 'onnx-community/whisper-tiny'
