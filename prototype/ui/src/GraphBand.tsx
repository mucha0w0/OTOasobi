import { useCallback, useEffect, useLayoutEffect, useRef, type RefObject } from "react"
import { bitmapRatio, canvasContext, shouldReplaceBitmap } from "./bitmap"
import { drawPeaks } from "./drawPeaks"
import { cycleOtoFocus, focusedOwnsTime, hitOtoPoint } from "./otoHit"
import { previewColumnLimit, previewRowLimit, timeAt, type OtoMark, type PreviewEvent } from "./protocol"
import { decodeBase64, paintIndices } from "./specTone"
import { hitBoundary, moveBoundary, spanBoundaries, spanCaption, spanFill, spanTimesChanged, type PhoneSpan } from "./spans"
import { xForTime, type PointerBus } from "./ruler"
import { imagePlacement, loudnessLevel, loudnessX, panRange, zoomRange, type Range } from "./viewport"

export type BandKind = "wave" | "spec" | "loud" | "oto"

export type BandHandle = {
  kind: BandKind
  holdLayout: (holding: boolean) => void
  redraw: () => void
  pixelSize: () => { columns: number; rows: number }
}

export type LiveTune = { index: number; point: number; time: number; moved: boolean }

export type GraphModel = {
  picture: PreviewEvent | null
  range: Range
  marks: OtoMark[]
  spans: PhoneSpan[]
  note: string
  focus: number
  live: LiveTune | null
  draft: PhoneSpan[] | null
}

type Props = {
  kind: BandKind
  model: RefObject<GraphModel>
  appearance: string
  register: (handle: BandHandle) => () => void
  onRange: (range: Range, commit: boolean) => void
  onFileStep: (direction: number) => void
  onTune?: (index: number, point: number, time: number) => void
  onFocus?: (index: number) => void
  onLive?: (live: LiveTune | null) => void
  onDraft?: (spans: PhoneSpan[] | null) => void
  onSpans?: (spans: PhoneSpan[]) => void
  pointerBus: PointerBus
}

const markColors = ["--mark-offset", "--mark-overlap", "--mark-preutter", "--mark-consonant", "--mark-blank"]
const markLabels = ["フ", "オ", "先", "子", "ブ"]

export function GraphBand({ kind, model, appearance, register, onRange, onFileStep, onTune, onFocus, onLive, onDraft, onSpans, pointerBus }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const pictureRef = useRef<HTMLCanvasElement>(null)
  const paintedRef = useRef<PreviewEvent | null>(null)
  const paintedSpecRef = useRef("")
  const paintedSizeRef = useRef({ width: 0, height: 0, color: "" })
  const bindPicture = useCallback((node: HTMLCanvasElement | null) => {
    pictureRef.current = node
    if (node)
      canvasContext(node)
  }, [])
  const bindCanvas = useCallback((node: HTMLCanvasElement | null) => {
    canvasRef.current = node
    if (node)
      canvasContext(node)
  }, [])
  const outerRef = useRef<HTMLDivElement>(null)
  const innerRef = useRef<HTMLDivElement>(null)
  const frozenRef = useRef<{ width: number; height: number } | null>(null)
  const kindRef = useRef(kind)
  const drawRef = useRef<() => void>(() => undefined)
  const applyHoldRef = useRef<(holding: boolean) => void>(() => undefined)
  kindRef.current = kind
  const lineRef = useRef<HTMLDivElement>(null)
  const holdRef = useRef(false)
  const dragRef = useRef<{ x: number; range: Range } | null>(null)
  const pressRef = useRef<{ index: number; point: number; x: number } | null>(null)
  const boundaryRef = useRef<{ group: number; origin: PhoneSpan[] } | null>(null)
  const onTuneRef = useRef(onTune)
  const onFocusRef = useRef(onFocus)
  const onLiveRef = useRef(onLive)
  const onDraftRef = useRef(onDraft)
  const onSpansRef = useRef(onSpans)
  onTuneRef.current = onTune
  onFocusRef.current = onFocus
  onLiveRef.current = onLive
  onDraftRef.current = onDraft
  onSpansRef.current = onSpans
  const wheelRef = useRef(0)
  const onRangeRef = useRef(onRange)
  const onFileStepRef = useRef(onFileStep)
  onRangeRef.current = onRange
  onFileStepRef.current = onFileStep

  useEffect(() => {
    const handle: BandHandle = {
      get kind() {
        return kindRef.current
      },
      holdLayout: (holding) => applyHoldRef.current(holding),
      redraw: () => drawRef.current(),
      pixelSize: () => ({
        columns: clamp(Math.round(canvasRef.current?.clientWidth ?? 800), 32, previewColumnLimit),
        rows: clamp(Math.round(canvasRef.current?.clientHeight ?? 256), 32, previewRowLimit),
      }),
    }
    return register(handle)
  }, [register])

  useLayoutEffect(() => {
    draw()
  }, [appearance])

  useEffect(() => {
    return pointerBus.watch((time) => placePointer(lineRef.current, canvasRef.current, model.current.range, time))
  }, [pointerBus, model])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas)
      return
    const frame = outerRef.current ?? canvas
    const observer = new ResizeObserver(() => {
      const frozen = frozenRef.current
      const outer = outerRef.current
      const inner = innerRef.current
      if (frozen && outer && inner) {
        scaleHoldFrame(outer, inner, frozen.width, frozen.height)
        return
      }
      drawRef.current()
    })
    observer.observe(frame)
    const wheel = (event: WheelEvent) => {
      const current = model.current
      event.preventDefault()
      if (!event.shiftKey && !event.ctrlKey) {
        wheelRef.current += event.deltaY
        const notches = Math.trunc(wheelRef.current / 120)
        wheelRef.current -= notches * 120
        if (notches !== 0)
          onFileStepRef.current(notches)
        return
      }
      if (!current.picture)
        return
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
      }
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

  function applyHold(holding: boolean) {
    const outer = outerRef.current
    const inner = innerRef.current
    if (!holding) {
      holdRef.current = false
      frozenRef.current = null
      if (inner)
        clearHoldFrame(inner)
      if (outer)
        void outer.offsetHeight
      drawRef.current()
      return
    }
    if (!outer || !inner || outer.clientWidth < 2 || outer.clientHeight < 2)
      return
    const width = outer.clientWidth
    const height = outer.clientHeight
    holdRef.current = true
    frozenRef.current = { width, height }
    pinHoldFrame(inner, width, height)
    scaleHoldFrame(outer, inner, width, height)
  }
  applyHoldRef.current = applyHold

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
    placePointer(lineRef.current, canvas, current.range, pointerBus.get())
    drawPicture(width, height, current.picture, current.note)
    drawOverlay(canvas, width, height, current)
  }
  drawRef.current = draw

  function drawPicture(width: number, height: number, pictureNow: PreviewEvent | null, note: string) {
    const canvas = pictureRef.current
    if (!canvas)
      return
    const source = pictureNow
      ? { t0: pictureNow.waveT0, t1: pictureNow.waveT1 }
      : { t0: 0, t1: 1 }
    const placed = pictureNow && kind !== "loud" ? imagePlacement(source, model.current.range, width) : { x: 0, width }
    canvas.style.left = `${placed.x}px`
    canvas.style.width = `${Math.max(1, placed.width)}px`
    canvas.style.height = `${height}px`
    canvas.style.maxWidth = "none"
    const color = getCss("--wave")
    canvas.style.imageRendering = "pixelated"
    if (
      kind === "spec"
      && pictureNow !== null
      && pictureNow.spec.length > 0
      && paintedSpecRef.current === pictureNow.spec
      && canvas.width > 0
    )
      return
    const same = pictureNow !== null
      && kind !== "spec"
      && paintedRef.current === pictureNow
      && paintedSizeRef.current.color === color
      && paintedSizeRef.current.width === width
      && paintedSizeRef.current.height === height
    if (kind !== "loud" && same && canvas.width > 0)
      return
    const ctx = canvasContext(canvas)
    if (!ctx)
      return
    ctx.setTransform(1, 0, 0, 1, 0, 0)
    if (!pictureNow || !paneReady(kind, pictureNow)) {
      canvas.width = Math.max(1, Math.floor(width))
      canvas.height = Math.max(1, Math.floor(height))
      ctx.fillStyle = getCss("--canvas")
      ctx.fillRect(0, 0, canvas.width, canvas.height)
      drawNote(ctx, canvas.width, canvas.height, note)
      paintedRef.current = null
      paintedSpecRef.current = ""
      paintedSizeRef.current = { width, height, color }
      return
    }
    if (kind === "spec" && pictureNow.specColumns > 0 && pictureNow.specRows > 0 && pictureNow.spec) {
      canvas.width = pictureNow.specColumns
      canvas.height = pictureNow.specRows
      if (!paintIndices(ctx, decodeBase64(pictureNow.spec), pictureNow.specColumns, pictureNow.specRows)) {
        ctx.fillStyle = getCss("--canvas")
        ctx.fillRect(0, 0, canvas.width, canvas.height)
        paintedSpecRef.current = ""
      } else {
        paintedSpecRef.current = pictureNow.spec
      }
    } else if (kind === "wave" || kind === "oto") {
      canvas.width = Math.max(1, Math.floor(width))
      canvas.height = Math.max(1, Math.floor(height))
      ctx.fillStyle = getCss("--canvas")
      ctx.fillRect(0, 0, canvas.width, canvas.height)
      drawPeaks(ctx, canvas.width, canvas.height, pictureNow.lo, pictureNow.hi, color)
    } else if (kind === "loud") {
      const nextWidth = Math.max(1, Math.floor(width))
      const nextHeight = Math.max(1, Math.floor(height))
      if (canvas.width !== nextWidth || canvas.height !== nextHeight) {
        canvas.width = nextWidth
        canvas.height = nextHeight
      }
      ctx.fillStyle = getCss("--canvas")
      ctx.fillRect(0, 0, canvas.width, canvas.height)
    } else {
      canvas.width = Math.max(1, Math.floor(width))
      canvas.height = Math.max(1, Math.floor(height))
      ctx.fillStyle = getCss("--canvas")
      ctx.fillRect(0, 0, canvas.width, canvas.height)
    }
    paintedRef.current = pictureNow
    paintedSizeRef.current = { width, height, color }
  }

  function drawOverlay(
    canvas: HTMLCanvasElement,
    width: number,
    height: number,
    current: GraphModel,
  ) {
    const ratio = bitmapRatio()
    const nextWidth = Math.max(1, Math.floor(width * ratio))
    const nextHeight = Math.max(1, Math.floor(height * ratio))
    if (shouldReplaceBitmap(false, canvas.width, canvas.height, nextWidth, nextHeight)) {
      canvas.width = nextWidth
      canvas.height = nextHeight
    }
    const ctx = canvasContext(canvas)
    if (!ctx)
      return
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0)
    ctx.clearRect(0, 0, width, height)
    const pictureNow = current.picture
    if (!pictureNow || !paneReady(kindRef.current, pictureNow))
      return
    const source = { t0: pictureNow.waveT0, t1: pictureNow.waveT1 }
    const placed = imagePlacement(source, current.range, width)
    const spans = current.draft ?? current.spans ?? []
    const shown = tunedMarks(current.marks, current.live)
    if (kind === "loud")
      drawLoud(ctx, width, height, pictureNow.loudness, pictureNow.duration, current.range, getCss("--wave"))
    if (kind === "wave" || kind === "spec")
      drawSpans(ctx, placed, source, spans, height, kind === "wave")
    if (kind === "wave" || kind === "spec" || kind === "loud") {
      ctx.save()
      if (current.focus >= 0)
        ctx.globalAlpha = 0.28
      drawBoundaries(ctx, placed, source, spans, height, kind === "wave")
      ctx.restore()
      const focused = shown.find((mark) => mark.index === current.focus)
      if (focused)
        drawPointLines(ctx, placed, source, focused, height, current.live, true)
    }
    if (kind === "oto")
      drawMarks(ctx, placed, source, shown, height, current.focus, current.live)
  }

  function endPointer() {
    const press = pressRef.current
    if (press) {
      pressRef.current = null
      const live = model.current.live
      if (live?.moved && live.index === press.index && live.point === press.point)
        onTuneRef.current?.(live.index, live.point, live.time)
      else
        onLiveRef.current?.(null)
    }
    const boundary = boundaryRef.current
    if (boundary) {
      boundaryRef.current = null
      const draft = model.current.draft
      if (draft && spanTimesChanged(boundary.origin, draft))
        onSpansRef.current?.(draft)
      else
        onDraftRef.current?.(null)
    }
    if (!dragRef.current)
      return
    dragRef.current = null
    onRangeRef.current(model.current.range, true)
  }

  function publishPointer(clientX: number, width: number, left: number) {
    const range = model.current.range
    if (!(range.t1 > range.t0) || width <= 0)
      return
    pointerBus.set(range.t0 + ((clientX - left) / width) * (range.t1 - range.t0))
  }

  return (
    <div
      ref={outerRef}
      data-graph=""
      className="relative h-full min-h-0 overflow-hidden bg-[var(--canvas)]"
      onPointerMove={(event) => {
        const rect = event.currentTarget.getBoundingClientRect()
        publishPointer(event.clientX, rect.width, rect.left)
      }}
      onPointerLeave={(event) => {
        const next = event.relatedTarget
        if (next instanceof Element && next.closest("[data-graph]"))
          return
        pointerBus.set(null)
      }}
    >
      <div ref={innerRef} className="relative h-full min-h-0">
      <canvas ref={bindPicture} className="pointer-events-none absolute top-0" />
      <div ref={lineRef} className="pointer-events-none absolute bottom-0 z-10 hidden w-px bg-[color-mix(in_srgb,var(--muted)_60%,transparent)]" style={{ top: 0 }} />
      <canvas
      ref={bindCanvas}
      tabIndex={0}
      className="absolute inset-0 h-full w-full touch-none"
      onContextMenu={(event) => event.preventDefault()}
      onPointerDown={(event) => {
        const current = model.current
        if (!current.picture)
          return
        const rect = event.currentTarget.getBoundingClientRect()
        const x = event.clientX - rect.left
        if (event.button === 0) {
          const marks = tunableMarks(kind, current)
          const hit = hitOtoPoint(marks, x, rect.width, current.range.t0, current.range.t1)
          const mark = hit == null ? null : marks[hit.index]
          if (hit && mark) {
            onFocusRef.current?.(mark.index)
            pressRef.current = { index: mark.index, point: hit.point, x: event.clientX }
            event.currentTarget.style.cursor = "ew-resize"
            event.currentTarget.setPointerCapture(event.pointerId)
            return
          }
          if (kind === "oto") {
            onFocusRef.current?.(cycleOtoFocus(current.marks, x, rect.width, current.range.t0, current.range.t1, current.focus))
            return
          }
          if (current.focus >= 0) {
            const time = timeAt(x, rect.width, current.range.t0, current.range.t1)
            if (!focusedOwnsTime(current.marks, current.spans, current.focus, time))
              onFocusRef.current?.(-1)
            return
          }
          const shown = current.draft ?? current.spans
          const group = hitBoundary(shown, x, rect.width, current.range.t0, current.range.t1)
          if (group >= 0) {
            boundaryRef.current = { group, origin: shown.map((span) => ({ ...span })) }
            event.currentTarget.style.cursor = "ew-resize"
            event.currentTarget.setPointerCapture(event.pointerId)
          }
          return
        }
        if (event.button !== 2)
          return
        dragRef.current = { x: event.clientX, range: current.range }
        event.currentTarget.setPointerCapture(event.pointerId)
      }}
      onPointerMove={(event) => {
        const current = model.current
        const boundary = boundaryRef.current
        if (boundary && current.picture) {
          const rect = event.currentTarget.getBoundingClientRect()
          const time = timeAt(event.clientX - rect.left, rect.width, current.range.t0, current.range.t1)
          onDraftRef.current?.(moveBoundary(boundary.origin, boundary.group, time, current.picture.duration))
          event.currentTarget.style.cursor = "ew-resize"
          return
        }
        const press = pressRef.current
        if (press && current.picture) {
          const rect = event.currentTarget.getBoundingClientRect()
          const moved = Math.abs(event.clientX - press.x) >= 3
          onLiveRef.current?.({
            index: press.index,
            point: press.point,
            time: timeAt(event.clientX - rect.left, rect.width, current.range.t0, current.range.t1),
            moved,
          })
          event.currentTarget.style.cursor = "ew-resize"
          return
        }
        if (current.picture) {
          const rect = event.currentTarget.getBoundingClientRect()
          const x = event.clientX - rect.left
          const onValue = kind === "oto" || current.focus >= 0
          const valueHit = onValue && hitOtoPoint(tunableMarks(kind, current), x, rect.width, current.range.t0, current.range.t1)
          const boundaryHit = !onValue && hitBoundary(current.draft ?? current.spans, x, rect.width, current.range.t0, current.range.t1) >= 0
          event.currentTarget.style.cursor = valueHit || boundaryHit ? "ew-resize" : "default"
        }
        const drag = dragRef.current
        if (!drag || !current.picture)
          return
        const width = event.currentTarget.getBoundingClientRect().width
        const span = drag.range.t1 - drag.range.t0
        const fraction = -(event.clientX - drag.x) / Math.max(1, width)
        onRangeRef.current(panRange(drag.range, fraction * (span / Math.max(span, 1e-12)), current.picture.duration), false)
      }}
      onPointerUp={() => endPointer()}
      onLostPointerCapture={() => endPointer()}
    />
      </div>
    </div>
  )
}

function placePointer(
  line: HTMLDivElement | null,
  canvas: HTMLCanvasElement | null,
  range: Range,
  time: number | null,
) {
  if (!line)
    return
  const width = canvas?.clientWidth ?? 0
  const x = time == null ? null : xForTime(time, range.t0, range.t1, width)
  if (x == null) {
    line.style.display = "none"
    return
  }
  line.style.display = "block"
  line.style.left = `${x}px`
}

function tunableMarks(kind: BandKind, model: GraphModel): OtoMark[] {
  if (kind === "oto")
    return model.marks
  if (model.focus < 0)
    return []
  return model.marks.filter((mark) => mark.index === model.focus)
}

function tunedMarks(marks: OtoMark[], tune: LiveTune | null) {
  if (!tune?.moved)
    return marks
  const keys = ["offset", "overlap", "preutter", "consonant", "blank"] as const
  const key = keys[tune.point]
  if (!key)
    return marks
  return marks.map((mark) => mark.index === tune.index ? { ...mark, [key]: tune.time } : mark)
}

function paneReady(kind: BandKind, picture: PreviewEvent) {
  if (kind === "spec")
    return picture.specColumns > 0 && picture.spec.length > 0
  if (kind === "loud")
    return picture.loudness.length > 0
  return picture.lo.length > 0
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
    ? getCss("--wave")
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
  focus: number,
  live: LiveTune | null,
) {
  const span = source.t1 - source.t0
  if (!(span > 0) || marks.length === 0)
    return
  const xAtTime = (time: number) => placed.x + ((time - source.t0) / span) * placed.width
  const dark = document.documentElement.dataset.theme === "dark"
  ctx.font = "12px sans-serif"
  ctx.textAlign = "center"
  ctx.textBaseline = "top"
  for (const mark of marks) {
    const chosen = focus >= 0 && mark.index === focus
    const dim = focus >= 0 && !chosen
    ctx.save()
    ctx.globalAlpha = dim ? 0.28 : 1
    const left = xAtTime(Math.min(mark.offset, mark.blank))
    const right = xAtTime(Math.max(mark.offset, mark.blank))
    ctx.fillStyle = "rgba(15, 157, 142, 0.25)"
    ctx.fillRect(left, 0, Math.max(1, right - left), height)
    strokePoints(ctx, xAtTime, mark, height, live, chosen)
    ctx.fillStyle = dark ? "#f9fafb" : "#1f2937"
    ctx.fillText(mark.alias, (left + right) / 2, 4, Math.max(12, right - left - 4))
    ctx.restore()
  }
}

function drawPointLines(
  ctx: CanvasRenderingContext2D,
  placed: { x: number; width: number },
  source: { t0: number; t1: number },
  mark: OtoMark,
  height: number,
  live: LiveTune | null,
  chosen: boolean,
) {
  const span = source.t1 - source.t0
  if (!(span > 0))
    return
  const xAtTime = (time: number) => placed.x + ((time - source.t0) / span) * placed.width
  strokePoints(ctx, xAtTime, mark, height, live, chosen)
}

function strokePoints(
  ctx: CanvasRenderingContext2D,
  xAtTime: (time: number) => number,
  mark: OtoMark,
  height: number,
  live: LiveTune | null,
  chosen: boolean,
) {
  const points = [mark.offset, mark.overlap, mark.preutter, mark.consonant, mark.blank]
  ctx.font = "11px Consolas, Cascadia Mono, monospace"
  ctx.textAlign = "center"
  ctx.textBaseline = "top"
  points.forEach((time, index) => {
    const dragging = live?.moved === true && live.index === mark.index && live.point === index
    const x = xAtTime(time)
    const color = getCss(markColors[index] ?? "--mark-offset")
    ctx.strokeStyle = color
    ctx.lineWidth = dragging ? 4 : chosen ? 3 : 2
    ctx.beginPath()
    ctx.moveTo(x, 0)
    ctx.lineTo(x, height)
    ctx.stroke()
    const width = ctx.canvas.clientWidth
    if (x < 8 || x > width - 8)
      return
    ctx.fillStyle = color
    ctx.fillText(markLabels[index] ?? "", x, 2)
  })
}

function drawLoud(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  values: number[],
  duration: number,
  range: Range,
  color: string,
) {
  if (values.length < 2 || !(duration > 0) || width <= 0 || height <= 0)
    return
  let peak = values[0] ?? 0
  for (const value of values)
    peak = Math.max(peak, value)
  ctx.strokeStyle = color
  ctx.lineWidth = 1
  ctx.beginPath()
  values.forEach((value, index) => {
    const x = loudnessX(index, values.length, duration, range, width)
    const y = height - 4 - loudnessLevel(value, peak) * (height - 8)
    if (index === 0)
      ctx.moveTo(x, y)
    else
      ctx.lineTo(x, y)
  })
  ctx.stroke()
}

function pinHoldFrame(inner: HTMLDivElement, width: number, height: number) {
  inner.style.position = "absolute"
  inner.style.left = "0"
  inner.style.top = "0"
  inner.style.width = `${width}px`
  inner.style.height = `${height}px`
  inner.style.transformOrigin = "0 0"
}

function scaleHoldFrame(outer: HTMLDivElement, inner: HTMLDivElement, width: number, height: number) {
  const nextWidth = outer.clientWidth
  const nextHeight = outer.clientHeight
  if (nextWidth <= 0 || nextHeight <= 0 || width <= 0 || height <= 0)
    return
  inner.style.transform = `scale(${nextWidth / width}, ${nextHeight / height})`
}

function clearHoldFrame(inner: HTMLDivElement) {
  inner.style.position = ""
  inner.style.left = ""
  inner.style.top = ""
  inner.style.width = ""
  inner.style.height = ""
  inner.style.transform = ""
  inner.style.transformOrigin = ""
}

function getCss(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || "#222"
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
}
