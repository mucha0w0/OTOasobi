import { useEffect, useRef, useState, type ReactNode, type RefObject } from "react"
import { shouldReplaceBitmap } from "./bitmap"
import { drawPeaks } from "./drawPeaks"
import { shownInputRate, type CaptureEvent, type PageCommand } from "./protocol"

export type LiveHandle = {
  show: (event: CaptureEvent) => void
}

type Props = {
  sink: RefObject<LiveHandle | null>
  send: (command: PageCommand) => void
  appearance: string
  children: ReactNode
}

export function LiveMonitor({ sink, send, appearance, children }: Props) {
  const [capture, setCapture] = useState<CaptureEvent | null>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const inputRate = shownInputRate(capture)
  const recording = capture?.recording ?? false

  useEffect(() => {
    sink.current = { show: setCapture }
    return () => {
      sink.current = null
    }
  }, [sink])

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
    <>
      {capture?.listenError && <p className="text-sm">{capture.listenError}</p>}
      <p className="text-sm text-[var(--muted)]">
        入力 {inputRate == null ? "—" : `${inputRate} Hz`}
        {recording ? " / 録音中" : ""}
      </p>
      <div className="h-16 rounded border border-[var(--line)] bg-[var(--canvas)]">
        <canvas ref={canvasRef} className="h-full w-full" />
      </div>
      <div className="h-2 overflow-hidden rounded bg-[var(--line)]">
        <div className="h-full bg-[var(--accent)]" style={{ width: `${Math.min(1, capture?.peak ?? 0) * 100}%` }} />
      </div>
      {children}
      <button
        type="button"
        className="accent-button"
        onClick={() => send(recording ? { type: "recordStop" } : { type: "record" })}
      >
        {recording ? "停止" : "録音"}
      </button>
    </>
  )
}

function getCss(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || "#222"
}
