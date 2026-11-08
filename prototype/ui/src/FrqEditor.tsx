import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from "react"
import { IconButton, IconPen } from "./IconButton"
import type { FrqViewEvent, PageCommand } from "./protocol"

const row = 14
const keyWidth = 54
const ruler = 20
const unvoiced = 22
const midiHigh = 127

function midiOf(hertz: number): number {
  return 69 + 12 * Math.log2(hertz / 440)
}

function noteName(midi: number): string {
  const names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
  const degree = ((midi % 12) + 12) % 12
  return `${names[degree]}${Math.floor(midi / 12) - 1}`
}

export function FrqEditor({
  view,
  send,
  appearance,
}: {
  view: FrqViewEvent | null
  send: (command: PageCommand) => void
  appearance: string
}) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [pen, setPen] = useState(false)
  const [selected, setSelected] = useState<number[]>([])
  const dragRef = useRef<{ x: number; y: number; points: { frame: number; midi: number; unvoiced: boolean }[] } | null>(null)
  const frequency = view?.frequency ?? []
  const hop = view?.hop ?? 0
  const rate = view?.sampleRate || 44100
  const pixels = frequency.length === 0 ? 4 : Math.max(2, Math.min(4, 3600 / frequency.length))
  const width = keyWidth + Math.max(1, frequency.length) * pixels
  const height = ruler + 128 * row + unvoiced

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas)
      return
    const ratio = window.devicePixelRatio || 1
    canvas.width = Math.floor(width * ratio)
    canvas.height = Math.floor(height * ratio)
    const ctx = canvas.getContext("2d")
    if (!ctx)
      return
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0)
    const dark = document.documentElement.dataset.theme === "dark"
    ctx.fillStyle = dark ? "#161616" : "#f4f4f5"
    ctx.fillRect(0, 0, width, height)
    ctx.fillStyle = dark ? "#111" : "#fff"
    ctx.fillRect(0, ruler, keyWidth, 128 * row)
    ctx.font = "11px sans-serif"
    ctx.textBaseline = "middle"
    for (let midi = 0; midi <= midiHigh; midi++) {
      const y = ruler + (midiHigh - midi) * row
      const black = [1, 3, 6, 8, 10].includes(midi % 12)
      ctx.fillStyle = black ? (dark ? "#2a2a2a" : "#e5e7eb") : (dark ? "#1c1c1c" : "#fafafa")
      ctx.fillRect(keyWidth, y, width - keyWidth, row)
      if (midi % 12 === 0) {
        ctx.fillStyle = dark ? "#e5e7eb" : "#111827"
        ctx.fillText(noteName(midi), 6, y + row / 2)
      }
    }
    ctx.fillStyle = dark ? "#111827" : "#f3f4f6"
    ctx.fillRect(keyWidth, ruler + 128 * row, width - keyWidth, unvoiced)
    ctx.fillStyle = dark ? "#d1d5db" : "#374151"
    ctx.fillText("なし", 8, ruler + 128 * row + unvoiced / 2)
    const chosen = new Set(selected)
    frequency.forEach((hz, frame) => {
      const x = keyWidth + frame * pixels
      const voiced = hz > 0 && Number.isFinite(hz)
      const y = voiced ? ruler + (midiHigh - midiOf(hz)) * row : ruler + 128 * row
      ctx.fillStyle = chosen.has(frame) ? (getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#0f766e") : (voiced ? "#fb923c" : "#9ca3af")
      ctx.fillRect(x, y + 4, Math.max(2, pixels - 1), 6)
    })
  }, [appearance, frequency, height, pixels, selected, width])

  function at(event: ReactPointerEvent<HTMLCanvasElement>) {
    const rect = event.currentTarget.getBoundingClientRect()
    const x = event.clientX - rect.left
    const y = event.clientY - rect.top
    const frame = Math.max(0, Math.min(frequency.length - 1, Math.floor((x - keyWidth) / pixels)))
    if (y >= ruler + 128 * row)
      return { frame, midi: 0, unvoiced: true }
    const midi = Math.max(0, Math.min(midiHigh, midiHigh - (y - ruler) / row))
    return { frame, midi, unvoiced: false }
  }

  function framesIn(x0: number, y0: number, x1: number, y1: number) {
    const left = Math.min(x0, x1)
    const right = Math.max(x0, x1)
    const top = Math.min(y0, y1)
    const bottom = Math.max(y0, y1)
    const frames: number[] = []
    frequency.forEach((hz, frame) => {
      const x = keyWidth + (frame + 0.5) * pixels
      const y = hz > 0 ? ruler + (midiHigh - midiOf(hz) + 0.5) * row : ruler + 128 * row + 8
      if (x >= left && x <= right && y >= top && y <= bottom)
        frames.push(frame)
    })
    return frames
  }

  const message = !view
    ? ""
    : view.error
      ? view.error
      : view.missing
        ? "このファイルの .frq はありません。"
        : ""

  return (
    <div className="flex h-full min-h-0 flex-col text-sm">
      <div className="flex items-center gap-2 border-b border-[var(--line)] px-3 py-2">
        <IconButton label="ペン" accent={pen} onClick={() => setPen((value) => !value)}>
          <IconPen />
        </IconButton>
        <button type="button" className="ghost-button" disabled={selected.length === 0} onClick={() => send({ type: "frq", op: "octave", frames: selected, octaves: 1 })}>
          +12
        </button>
        <button type="button" className="ghost-button" disabled={selected.length === 0} onClick={() => send({ type: "frq", op: "octave", frames: selected, octaves: -1 })}>
          -12
        </button>
        <button type="button" className="ghost-button" disabled={selected.length === 0} onClick={() => send({ type: "frq", op: "clear", frames: selected })}>
          −
        </button>
        {hop > 0 && <span className="num text-[var(--muted)]">{(hop / rate).toFixed(4)} 秒 / 点</span>}
      </div>
      <div className="relative min-h-0 flex-1 overflow-auto">
        {message && <p className="p-3 text-[var(--muted)]">{message}</p>}
        {frequency.length > 0 && (
          <canvas
            ref={canvasRef}
            className={pen ? "frq-pen touch-none" : "frq-select touch-none"}
            style={{ width, height }}
            onPointerDown={(event) => {
              if (event.button !== 0)
                return
              const point = at(event)
              dragRef.current = { x: event.clientX, y: event.clientY, points: [point] }
              event.currentTarget.setPointerCapture(event.pointerId)
            }}
            onPointerMove={(event) => {
              const drag = dragRef.current
              if (!drag || !pen)
                return
              const point = at(event)
              const last = drag.points[drag.points.length - 1]
              if (!last || last.frame !== point.frame || last.unvoiced !== point.unvoiced)
                drag.points.push(point)
            }}
            onPointerUp={(event) => {
              const drag = dragRef.current
              dragRef.current = null
              if (!drag)
                return
              if (pen) {
                send({ type: "frq", op: "stroke", points: drag.points })
                return
              }
              const rect = event.currentTarget.getBoundingClientRect()
              setSelected(framesIn(drag.x - rect.left, drag.y - rect.top, event.clientX - rect.left, event.clientY - rect.top))
            }}
          />
        )}
      </div>
    </div>
  )
}
