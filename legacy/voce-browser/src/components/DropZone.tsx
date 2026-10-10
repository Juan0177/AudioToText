import { useRef, useState, type ChangeEvent, type DragEvent } from 'react'
import { ACCEPTED_AUDIO } from '../lib/audio'

type DropZoneProps = {
  disabled?: boolean
  onFile: (file: File) => void
}

export function DropZone({ disabled, onFile }: DropZoneProps) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [dragging, setDragging] = useState(false)

  const handleFiles = (files: FileList | null) => {
    const file = files?.[0]
    if (file) onFile(file)
  }

  const onDrop = (event: DragEvent<HTMLLabelElement>) => {
    event.preventDefault()
    setDragging(false)
    if (disabled) return
    handleFiles(event.dataTransfer.files)
  }

  const onChange = (event: ChangeEvent<HTMLInputElement>) => {
    handleFiles(event.target.files)
    event.target.value = ''
  }

  return (
    <label
      onDragEnter={(event) => {
        event.preventDefault()
        if (!disabled) setDragging(true)
      }}
      onDragOver={(event) => event.preventDefault()}
      onDragLeave={() => setDragging(false)}
      onDrop={onDrop}
      className={`group relative flex min-h-44 cursor-pointer flex-col items-center justify-center rounded-2xl border border-dashed px-6 py-8 text-center transition duration-300 ${
        dragging
          ? 'border-honey bg-honey/10'
          : 'border-mist/35 bg-deep/45 hover:border-mist/70 hover:bg-deep/70'
      } ${disabled ? 'pointer-events-none opacity-55' : ''}`}
    >
      <input
        ref={inputRef}
        type="file"
        accept={ACCEPTED_AUDIO}
        className="sr-only"
        disabled={disabled}
        onChange={onChange}
      />
      <span className="font-display text-xl font-bold text-foam sm:text-2xl">
        Trascina qui il vocale
      </span>
      <span className="mt-2 max-w-sm text-sm text-muted">
        Oppure tocca per scegliere un file esportato da WhatsApp (.ogg, .opus, .m4a,
        .mp3, .wav, .webm)
      </span>
    </label>
  )
}
