const BARS = [0.35, 0.7, 1, 0.55, 0.9, 0.45, 0.8, 0.6, 1, 0.5, 0.75, 0.4]

type WaveformProps = {
  active?: boolean
}

export function Waveform({ active = false }: WaveformProps) {
  return (
    <div
      className="pointer-events-none absolute inset-0 overflow-hidden"
      aria-hidden="true"
    >
      <div className="glow-orb absolute -left-16 top-10 h-64 w-64 rounded-full bg-[radial-gradient(circle,rgba(126,200,184,0.28),transparent_68%)] blur-2xl" />
      <div className="glow-orb absolute -right-10 bottom-0 h-72 w-72 rounded-full bg-[radial-gradient(circle,rgba(232,184,109,0.22),transparent_70%)] blur-2xl [animation-delay:-4s]" />

      <div className="absolute inset-x-0 bottom-[18%] flex h-28 items-end justify-center gap-1.5 opacity-70 sm:gap-2">
        {BARS.map((height, index) => (
          <span
            key={index}
            className={`wave-bar w-1.5 rounded-full bg-gradient-to-t from-mist/30 to-honey/90 sm:w-2 ${
              active ? '' : '[animation-play-state:paused]'
            }`}
            style={{
              height: `${height * 100}%`,
              animationDelay: `${index * 0.08}s`,
            }}
          />
        ))}
      </div>
    </div>
  )
}
