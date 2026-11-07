import { useEffect, useRef, type PointerEvent as ReactPointerEvent, type RefObject } from "react"
import { shouldReplaceBitmap } from "./bitmap"
import { drawPeaks } from "./drawPeaks"
import { requestsPreview, timeAt, xAt, type PreviewEvent, type PreviewGesture } from "./protocol"

const hitPx = 8

export type WaveHandle = {
  pixelSize: () => { columns: number; rows: number }
  holdLayout: (holding: boolean) => void
}

type Props = {
  picture: PreviewEvent | null
  boundary: number
  appearance: string
  onBoundary: (time: number, commit: boolean) => void
  onPreview: (gesture: PreviewGesture, t0: number, t1: number) => void
  onPointerTime: (time: number | null) => void
  handle: RefObject<WaveHandle | null>
}

type Drag =
  | { kind: "none" }
  | { kind: "boundary" }
  | { kind: "pan"; x: number; t0: number; t1: number }

export function WaveView({ picture, boundary, appearance, onBoundary, onPreview, onPointerTime, handle }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const imageRef = useRef<HTMLImageElement | null>(null)
  const panRef = useRef(0)
  const pendingRef = useRef<{ t0: number; t1: number } | null>(null)
  const dragRef = useRef<Drag>({ kind: "none" })
  const holdRef = useRef(false)
  const pictureRef = useRef(picture)
  const boundaryRef = useRef(boundary)
  pictureRef.current = picture
  if (dragRef.current.kind !== "boundary")
    boundaryRef.current = boundary

  useEffect(() => {
    panRef.current = 0
    pendingRef.current = picture ? { t0: picture.waveT0, t1: picture.waveT1 } : null
    draw()
  }, [picture])

  useEffect(() => {
    draw()
  }, [appearance, boundary])

  useEffect(() => {
    if (!picture?.png) {
      imageRef.current = null
      draw()
      return
    }
    const image = new Image()
    image.onload = () => {
      imageRef.current = image
      draw()
    }
    image.src = `data:image/png;base64,${picture.png}`
  }, [picture?.png])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas)
      return
    const observer = new ResizeObserver(() => draw())
    observer.observe(canvas)
    return () => observer.disconnect()
  }, [])

  useEffect(() => {
    handle.current = {
      pixelSize: () => {
        const canvas = canvasRef.current
        return {
          columns: clamp(Math.round(canvas?.clientWidth ?? 800), 32, 4096),
          rows: clamp(Math.round(canvas?.clientHeight ?? 320), 32, 1200),
        }
      },
      holdLayout: (holding) => {
        holdRef.current = holding
        if (!holding)
          draw()
      },
    }
    return () => {
      handle.current = null
    }
  }, [handle])

  function displayedRange() {
    const current = pictureRef.current
    if (!current)
      return { t0: 0, t1: 1, pan: 0 }
    return { t0: current.waveT0, t1: current.waveT1, pan: panRef.current }
  }

  function draw() {
    if (holdRef.current)
      return
    const canvas = canvasRef.current
    const current = pictureRef.current
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
    ctx.fillStyle = getCss("--canvas")
    ctx.fillRect(0, 0, width, height)
    const pan = panRef.current
    const image = imageRef.current
    if (image)
      ctx.drawImage(image, pan, 0, width, height)
    if (current)
      drawPeaks(ctx, width, height, current.lo, current.hi, getCss("--wave"), pan)
    if (current) {
      const x = xAt(boundaryRef.current, width, current.waveT0, current.waveT1, pan)
      ctx.strokeStyle = getCss("--boundary")
      ctx.lineWidth = 2
      ctx.beginPath()
      ctx.moveTo(x, 0)
      ctx.lineTo(x, height)
      ctx.stroke()
    }
  }

  function pointerTime(event: ReactPointerEvent<HTMLCanvasElement>) {
    const rect = event.currentTarget.getBoundingClientRect()
    const range = displayedRange()
    return timeAt(event.clientX - rect.left, rect.width, range.t0, range.t1, range.pan)
  }

  return (
    <canvas
      ref={canvasRef}
      className="h-full w-full touch-none"
      onContextMenu={(event) => event.preventDefault()}
      onPointerDown={(event) => {
        const current = pictureRef.current
        if (!current)
          return
        const rect = event.currentTarget.getBoundingClientRect()
        const x = event.clientX - rect.left
        const boundaryX = xAt(boundaryRef.current, rect.width, current.waveT0, current.waveT1, panRef.current)
        if (event.button === 0 && Math.abs(x - boundaryX) <= hitPx)
          dragRef.current = { kind: "boundary" }
        else if (event.button === 2 || event.shiftKey)
          dragRef.current = { kind: "pan", x, t0: current.waveT0, t1: current.waveT1 }
        else
          return
        event.currentTarget.setPointerCapture(event.pointerId)
      }}
      onPointerMove={(event) => {
        const time = pointerTime(event)
        onPointerTime(time)
        const drag = dragRef.current
        if (drag.kind === "boundary") {
          onBoundary(time, false)
          boundaryRef.current = time
          draw()
          return
        }
        if (drag.kind === "pan") {
          panRef.current = event.clientX - event.currentTarget.getBoundingClientRect().left - drag.x
          draw()
        }
      }}
      onPointerUp={(event) => {
        const drag = dragRef.current
        dragRef.current = { kind: "none" }
        if (drag.kind === "boundary") {
          const time = pointerTime(event)
          boundaryRef.current = time
          onBoundary(time, true)
          draw()
          return
        }
        if (drag.kind !== "pan" || !requestsPreview("pan"))
          return
        const rect = event.currentTarget.getBoundingClientRect()
        const span = drag.t1 - drag.t0
        const dt = (-panRef.current / Math.max(1, rect.width)) * span
        onPreview("pan", drag.t0 + dt, drag.t1 + dt)
      }}
      onPointerLeave={() => onPointerTime(null)}
      onWheel={(event) => {
        const current = pictureRef.current
        if (!current || !requestsPreview("zoom"))
          return
        event.preventDefault()
        const rect = event.currentTarget.getBoundingClientRect()
        const base = pendingRef.current ?? { t0: current.waveT0, t1: current.waveT1 }
        const x = event.clientX - rect.left
        const anchor = timeAt(x, rect.width, base.t0, base.t1, 0)
        const span = Math.max(0.01, base.t1 - base.t0)
        const next = span * (event.deltaY > 0 ? 1.15 : 0.87)
        const ratio = (anchor - base.t0) / span
        const t0 = anchor - next * ratio
        const t1 = t0 + next
        pendingRef.current = { t0, t1 }
        onPreview("zoom", t0, t1)
      }}
    />
  )
}

function getCss(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || "#222"
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
}
