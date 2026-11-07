import { useEffect, useRef, useState } from "react"
import { shouldReplaceBitmap } from "./bitmap"
import { drawPeaks } from "./drawPeaks"
import { shownInputRate, type CaptureEvent, type PageCommand, type SavedEvent } from "./protocol"
import type { LiveBus } from "./live"

const notes = ["C3", "D3", "E3", "F3", "G3", "A3", "B3", "C4", "D4", "E4", "F4", "G4", "A4", "B4", "C5", "D5", "E5", "F5", "G5", "A5", "B5", "C6"]

export function RecPanel({
  bus,
  saved,
  send,
}: {
  bus: LiveBus
  saved: SavedEvent | null
  send: (command: PageCommand) => void
}) {
  const capture = useCapture(bus)
  const recording = capture?.recording ?? false
  const inputRate = shownInputRate(capture)
  return (
    <div className="flex h-full flex-col gap-3 overflow-auto p-3 text-sm">
      <p className="text-[var(--muted)]">
        入力 {inputRate == null ? "—" : `${inputRate} Hz`}
        {recording ? " / 録音中" : ""}
      </p>
      <button type="button" className="accent-button" onClick={() => send(recording ? { type: "recordStop" } : { type: "record" })}>
        {recording ? "停止" : "録音"}
      </button>
      {saved && (
        <p className="leading-6">
          入力 {saved.inputRate} Hz、ヘッダ {saved.headerRate} Hz / {saved.bits} bit / {saved.channels} ch、
          ファイル {saved.seconds.toFixed(2)} 秒、操作 {saved.elapsed.toFixed(2)} 秒
        </p>
      )}
    </div>
  )
}

export function GuidePanel({
  note,
  semitones,
  speed,
  playing,
  setNote,
  setSemitones,
  setSpeed,
  send,
}: {
  note: string
  semitones: number
  speed: number
  playing: boolean
  setNote: (value: string) => void
  setSemitones: (value: number) => void
  setSpeed: (value: number) => void
  send: (command: PageCommand) => void
}) {
  return (
    <div className="flex h-full flex-col gap-3 overflow-auto p-3 text-sm">
      <label className="grid gap-1">
        ガイドの音
        <select className="field" value={note} onChange={(event) => setNote(event.target.value)}>
          {notes.map((item) => (
            <option key={item}>{item}</option>
          ))}
        </select>
      </label>
      <label className="grid gap-1">
        音程 {semitones} 半音
        <input type="range" min={-12} max={12} step={1} value={semitones} onChange={(event) => setSemitones(Number(event.target.value))} />
      </label>
      <label className="grid gap-1">
        速さ {Math.round(speed * 100)}%
        <input type="range" min={0.5} max={2} step={0.05} value={speed} onChange={(event) => setSpeed(Number(event.target.value))} />
      </label>
      <button
        type="button"
        className="accent-button"
        onClick={() => send(playing ? { type: "guideStop" } : { type: "guide", note, semitones, speed })}
      >
        {playing ? "ガイドを停止" : "ガイドを聴く"}
      </button>
    </div>
  )
}

export function MicPanel({ bus, appearance, monitor, setMonitor, send }: {
  bus: LiveBus
  appearance: string
  monitor: boolean
  setMonitor: (on: boolean) => void
  send: (command: PageCommand) => void
}) {
  const capture = useCapture(bus)
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas)
      return
    const width = canvas.clientWidth
    const height = canvas.clientHeight
    if (width <= 0 || height <= 0)
      return
    const ratio = window.devicePixelRatio || 1
    const nextWidth = Math.max(1, Math.floor(width * ratio))
    const nextHeight = Math.max(1, Math.floor(height * ratio))
    if (shouldReplaceBitmap(false, canvas.width, canvas.height, nextWidth, nextHeight)) {
      canvas.width = nextWidth
      canvas.height = nextHeight
    }
    const ctx = canvas.getContext("2d")
    if (!ctx)
      return
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0)
    ctx.clearRect(0, 0, width, height)
    if (capture)
      drawPeaks(ctx, width, height, capture.lo, capture.hi, getCss("--wave"))
  }, [capture, appearance])

  return (
    <div className="flex h-full flex-col gap-3 p-3 text-sm">
      <label className="flex items-center gap-2">
        <input
          type="checkbox"
          checked={monitor}
          onChange={(event) => {
            setMonitor(event.target.checked)
            send({ type: "monitor", on: event.target.checked })
          }}
        />
        入力を聴く
      </label>
      {capture?.listenError && <p>{capture.listenError}</p>}
      <div className="min-h-16 flex-1 rounded border border-[var(--line)] bg-[var(--canvas)]">
        <canvas ref={canvasRef} className="h-full w-full" />
      </div>
      <div className="h-2 overflow-hidden rounded bg-[var(--line)]">
        <div className="h-full bg-[var(--accent)]" style={{ width: `${Math.min(1, capture?.peak ?? 0) * 100}%` }} />
      </div>
    </div>
  )
}

function useCapture(bus: LiveBus) {
  const [capture, setCapture] = useState<CaptureEvent | null>(null)
  useEffect(() => bus.listen(setCapture), [bus])
  return capture
}

function getCss(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || "#222"
}
