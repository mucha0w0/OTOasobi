import { useEffect, useRef, type RefObject } from "react"
import { shouldReplaceBitmap } from "./bitmap"
import { drawPeaks } from "./drawPeaks"
import { hitOtoPoint } from "./otoHit"
import { timeAt, type OtoMark, type PreviewEvent } from "./protocol"
import { spanBoundaries, spanCaption, spanFill, type PhoneSpan } from "./spans"
import { imagePlacement, panRange, zoomRange, type Range } from "./viewport"

export type BandKind = "wave" | "spec" | "loud" | "oto"

export type BandHandle = {
  kind: BandKind
  holdLayout: (holding: boolean) => void
  redraw: () => void
  pixelSize: () => { columns: number; rows: number }
}

export type GraphModel = {
  picture: PreviewEvent | null
  range: Range
  marks: OtoMark[]
  spans: PhoneSpan[]
  note: string
}

type Props = {
  kind: BandKind
  picture: PreviewEvent | null
  model: RefObject<GraphModel>
  appearance: string
  register: (handle: BandHandle) => () => void
  onRange: (range: Range, commit: boolean) => void
  onFileStep: (direction: number) => void
  onTune?: (index: number, point: number, time: number) => void
}

export function GraphBand({ kind, picture, model, appearance, register, onRange, onFileStep, onTune }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const imageRef = useRef<HTMLImageElement | null>(null)
  const holdRef = useRef(false)
  const dragRef = useRef<{ x: number; range: Range } | null>(null)
  const tuneRef = useRef<{ index: number; point: number; time: number } | null>(null)
  const onTuneRef = useRef(onTune)
  onTuneRef.current = onTune
  const wheelRef = useRef(0)
  const onRangeRef = useRef(onRange)
  const onFileStepRef = useRef(onFileStep)
  onRangeRef.current = onRange
  onFileStepRef.current = onFileStep

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas)
      return
    const handle: BandHandle = {
      kind,
      holdLayout: (holding) => {
        holdRef.current = holding
        if (!holding)
          draw()
      },
      redraw: () => draw(),
      pixelSize: () => ({
        columns: clamp(Math.round(canvas.clientWidth), 32, 4096),
        rows: clamp(Math.round(canvas.clientHeight), 32, 1200),
      }),
    }
    return register(handle)
  }, [register])

  useEffect(() => {
    if (kind !== "spec" || !picture?.png) {
      if (kind === "spec")
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
  }, [kind, picture?.png])

  useEffect(() => {
    draw()
  }, [picture, appearance])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas)
      return
    const observer = new ResizeObserver(() => draw())
    observer.observe(canvas)
    const wheel = (event: WheelEvent) => {
      const current = model.current
      if (!current.picture)
        return
      event.preventDefault()
      const rect = canvas.getBoundingClientRect()
      const x = event.clientX - rect.left
      const range = current.range
      if (event.shiftKey) {
        const fraction = (event.deltaY / 120) * 0.15
        onRangeRef.current(panRange(range, fraction, current.picture.duration), false)
        return
      }
      if (event.ctrlKey) {
        const anchor = timeAt(x, rect.width, range.t0, range.t1)
        const factor = event.deltaY < 0 ? 0.8 : 1.25
        onRangeRef.current(zoomRange(range, anchor, factor, current.picture.duration), false)
        return
      }
      wheelRef.current += event.deltaY
      const notches = Math.trunc(wheelRef.current / 120)
      wheelRef.current -= notches * 120
      if (notches !== 0)
        onFileStepRef.current(Math.sign(notches))
    }
    const keys = (event: KeyboardEvent) => {
      const current = model.current
      if (!current.picture)
        return
      if (event.key !== "ArrowLeft" && event.key !== "ArrowRight")
        return
      event.preventDefault()
      onRangeRef.current(panRange(current.range, event.key === "ArrowLeft" ? -0.2 : 0.2, current.picture.duration), false)
    }
    canvas.addEventListener("wheel", wheel, { passive: false })
    canvas.addEventListener("keydown", keys)
    return () => {
      observer.disconnect()
      canvas.removeEventListener("wheel", wheel)
      canvas.removeEventListener("keydown", keys)
    }
  }, [model])

  function draw() {
    if (holdRef.current)
      return
    const canvas = canvasRef.current
    const current = model.current
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
    const pictureNow = current.picture
    if (!pictureNow) {
      drawNote(ctx, width, height, current.note ?? "")
      return
    }
    const source = { t0: pictureNow.waveT0, t1: pictureNow.waveT1 }
    const placed = imagePlacement(source, current.range, width)
    if (kind === "spec" && imageRef.current)
      ctx.drawImage(imageRef.current, placed.x, 0, placed.width, height)
    if (kind === "wave" || kind === "oto")
      drawPeaks(ctx, placed.width, height, pictureNow.lo, pictureNow.hi, getCss("--wave"), placed.x)
    if (kind === "loud")
      drawLoud(ctx, placed, pictureNow.loudness, getCss("--wave"))
    const spans = current.spans ?? []
    if (kind === "wave" || kind === "spec")
      drawSpans(ctx, placed, source, spans, height, kind === "wave")
    if (kind === "wave" || kind === "spec" || kind === "loud")
      drawBoundaries(ctx, placed, source, spans, height, kind === "wave")
    if (kind === "oto")
      drawMarks(ctx, placed, source, tunedMarks(current.marks, tuneRef.current), height)
  }

  return (
    <canvas
      ref={canvasRef}
      tabIndex={0}
      className="h-full w-full touch-none outline-none"
      onContextMenu={(event) => event.preventDefault()}
      onPointerDown={(event) => {
        const current = model.current
        if (!current.picture)
          return
        if (event.button === 0 && kind === "oto") {
          const rect = event.currentTarget.getBoundingClientRect()
          const hit = hitOtoPoint(current.marks, event.clientX - rect.left, rect.width, current.range.t0, current.range.t1)
          const mark = hit == null ? null : current.marks[hit.index]
          if (!hit || !mark)
            return
          const times = [mark.offset, mark.overlap, mark.preutter, mark.consonant, mark.blank]
          tuneRef.current = { index: mark.index, point: hit.point, time: times[hit.point] ?? 0 }
          event.currentTarget.setPointerCapture(event.pointerId)
          return
        }
        if (event.button !== 2)
          return
        dragRef.current = { x: event.clientX, range: current.range }
        event.currentTarget.setPointerCapture(event.pointerId)
      }}
      onPointerMove={(event) => {
        const tune = tuneRef.current
        const current = model.current
        if (tune && current.picture) {
          const rect = event.currentTarget.getBoundingClientRect()
          tuneRef.current = { ...tune, time: timeAt(event.clientX - rect.left, rect.width, current.range.t0, current.range.t1) }
          draw()
          return
        }
        if (kind === "oto" && current.picture) {
          const rect = event.currentTarget.getBoundingClientRect()
          const hit = hitOtoPoint(current.marks, event.clientX - rect.left, rect.width, current.range.t0, current.range.t1)
          event.currentTarget.style.cursor = hit ? "ew-resize" : "default"
        }
        const drag = dragRef.current
        if (!drag || !current.picture)
          return
        const width = event.currentTarget.clientWidth
        const span = drag.range.t1 - drag.range.t0
        const fraction = -(event.clientX - drag.x) / Math.max(1, width)
        onRangeRef.current(panRange(drag.range, fraction * (span / Math.max(span, 1e-12)), current.picture.duration), false)
      }}
      onPointerUp={() => {
        const tune = tuneRef.current
        if (tune) {
          tuneRef.current = null
          onTuneRef.current?.(tune.index, tune.point, tune.time)
          draw()
          return
        }
        if (!dragRef.current)
          return
        dragRef.current = null
        onRangeRef.current(model.current.range, true)
      }}
    />
  )
}

function tunedMarks(marks: OtoMark[], tune: { index: number; point: number; time: number } | null) {
  if (!tune)
    return marks
  const keys = ["offset", "overlap", "preutter", "consonant", "blank"] as const
  const key = keys[tune.point]
  if (!key)
    return marks
  return marks.map((mark) => mark.index === tune.index ? { ...mark, [key]: tune.time } : mark)
}

function drawNote(ctx: CanvasRenderingContext2D, width: number, height: number, note: string) {
  if (!note)
    return
  ctx.fillStyle = getCss("--muted")
  ctx.font = "14px sans-serif"
  ctx.textAlign = "center"
  ctx.textBaseline = "middle"
  ctx.fillText(note, width / 2, height / 2)
}

function drawSpans(
  ctx: CanvasRenderingContext2D,
  placed: { x: number; width: number },
  source: { t0: number; t1: number },
  spans: PhoneSpan[],
  height: number,
  wave: boolean,
) {
  const span = source.t1 - source.t0
  if (!(span > 0) || spans.length === 0)
    return
  const xAtTime = (time: number) => placed.x + ((time - source.t0) / span) * placed.width
  const dark = document.documentElement.dataset.theme === "dark"
  ctx.font = "12px sans-serif"
  ctx.textAlign = "center"
  ctx.textBaseline = "top"
  for (const item of spans) {
    const fill = spanFill(item.label, wave)
    if (!fill)
      continue
    const left = xAtTime(item.start)
    const right = xAtTime(item.end)
    const width = right - left
    if (!(width > 0))
      continue
    ctx.fillStyle = fill
    ctx.fillRect(left, 0, width, height)
    if (width < 28)
      continue
    ctx.fillStyle = dark ? "#f9fafb" : "#1f2937"
    ctx.fillText(spanCaption(item), (left + right) / 2, 8, Math.max(12, width - 4))
  }
}

function drawBoundaries(
  ctx: CanvasRenderingContext2D,
  placed: { x: number; width: number },
  source: { t0: number; t1: number },
  spans: PhoneSpan[],
  height: number,
  wave: boolean,
) {
  const span = source.t1 - source.t0
  if (!(span > 0))
    return
  const dark = document.documentElement.dataset.theme === "dark"
  ctx.strokeStyle = wave
    ? (dark ? "rgb(236, 236, 236)" : "rgb(32, 32, 36)")
    : (dark ? "rgba(255, 255, 255, 0.9)" : "rgba(17, 24, 39, 0.9)")
  ctx.lineWidth = 1
  for (const time of spanBoundaries(spans)) {
    const x = placed.x + ((time - source.t0) / span) * placed.width
    ctx.beginPath()
    ctx.moveTo(x, 0)
    ctx.lineTo(x, height)
    ctx.stroke()
  }
}

function drawMarks(
  ctx: CanvasRenderingContext2D,
  placed: { x: number; width: number },
  source: { t0: number; t1: number },
  marks: OtoMark[],
  height: number,
) {
  const span = source.t1 - source.t0
  if (!(span > 0) || marks.length === 0)
    return
  const xAtTime = (time: number) => placed.x + ((time - source.t0) / span) * placed.width
  const dark = document.documentElement.dataset.theme === "dark"
  const colors = [dark ? "#ececec" : "#374151", "#fb923c", "#f87171", "#60a5fa", "#c4b5fd"]
  ctx.font = "12px sans-serif"
  ctx.textAlign = "center"
  ctx.textBaseline = "top"
  for (const mark of marks) {
    const left = xAtTime(Math.min(mark.offset, mark.blank))
    const right = xAtTime(Math.max(mark.offset, mark.blank))
    ctx.fillStyle = "rgba(15, 157, 142, 0.25)"
    ctx.fillRect(left, 0, Math.max(1, right - left), height)
    const points = [mark.offset, mark.overlap, mark.preutter, mark.consonant, mark.blank]
    points.forEach((time, index) => {
      const x = xAtTime(time)
      ctx.strokeStyle = colors[index] ?? colors[0]!
      ctx.lineWidth = 2
      ctx.beginPath()
      ctx.moveTo(x, 0)
      ctx.lineTo(x, height)
      ctx.stroke()
    })
    ctx.fillStyle = dark ? "#f9fafb" : "#1f2937"
    ctx.fillText(mark.alias, (left + right) / 2, 4, Math.max(12, right - left - 4))
  }
}

function drawLoud(
  ctx: CanvasRenderingContext2D,
  placed: { x: number; width: number },
  values: number[],
  color: string,
) {
  if (values.length < 2 || placed.width === 0)
    return
  let low = values[0] ?? 0
  let high = low
  for (const value of values) {
    low = Math.min(low, value)
    high = Math.max(high, value)
  }
  const span = Math.max(1e-6, high - low)
  const height = ctx.canvas.clientHeight
  ctx.strokeStyle = color
  ctx.beginPath()
  values.forEach((value, index) => {
    const x = placed.x + (index / (values.length - 1)) * placed.width
    const y = height - ((value - low) / span) * (height - 8) - 4
    if (index === 0)
      ctx.moveTo(x, y)
    else
      ctx.lineTo(x, y)
  })
  ctx.stroke()
}

function getCss(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || "#222"
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
}
