import { useCallback, useEffect, useLayoutEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from "react"
import { bitmapRatio, canvasContext } from "./bitmap"
import { IconButton, IconPen } from "./IconButton"
import { frqRuler, frqRow, frqUnvoiced, isBlack, midiOf, noteName, pitchSpan } from "./frqRoll"
import type { FrqViewEvent, PageCommand } from "./protocol"

function localFrame(origin: HTMLDivElement, clientX: number, scrollLeft: number, pixels: number, length: number) {
  const rect = origin.getBoundingClientRect()
  const x = clientX - rect.left + scrollLeft
  return Math.max(0, Math.min(length - 1, Math.floor(x / pixels)))
}

function keyColor(midi: number, dark: boolean) {
  if (dark)
    return isBlack(midi) ? "#141416" : "#3a3a40"
  return isBlack(midi) ? "#e5e7eb" : "#fafafa"
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
  const pianoRef = useRef<HTMLCanvasElement>(null)
  const laneRef = useRef<HTMLCanvasElement>(null)
  const xScrollRef = useRef<HTMLDivElement>(null)
  const yScrollRef = useRef<HTMLDivElement>(null)
  const laneBoxRef = useRef<HTMLDivElement>(null)
  const scrollXRef = useRef(0)
  const bindPiano = useCallback((node: HTMLCanvasElement | null) => {
    pianoRef.current = node
    if (node)
      canvasContext(node)
  }, [])
  const bindLane = useCallback((node: HTMLCanvasElement | null) => {
    laneRef.current = node
    if (node)
      canvasContext(node)
  }, [])
  const [pen, setPen] = useState(false)
  const [selected, setSelected] = useState<number[]>([])
  const [scrollX, setScrollX] = useState(0)
  const [scrollY, setScrollY] = useState(0)
  const [trackWidth, setTrackWidth] = useState(0)
  const dragRef = useRef<{ x: number; y: number; points: { frame: number; midi: number; unvoiced: boolean }[] } | null>(null)
  const spanRef = useRef({ name: "", high: 0 })
  const frequency = view?.frequency ?? []
  const hop = view?.hop ?? 0
  const rate = view?.sampleRate || 44100
  const pixels = frequency.length === 0 ? 4 : Math.max(2, Math.min(4, 3600 / frequency.length))
  const span = pitchSpan(frequency)
  const width = Math.max(1, frequency.length) * pixels
  const pianoHeight = frqRuler + (span.high - span.low + 1) * frqRow
  const cNotes: number[] = []
  for (let midi = Math.ceil(span.low / 12) * 12; midi <= span.high; midi += 12)
    cNotes.push(midi)

  useEffect(() => {
    setSelected([])
  }, [view?.name])

  useEffect(() => {
    const yScroll = yScrollRef.current
    const xScroll = xScrollRef.current
    if (!yScroll || !view?.name || frequency.length === 0)
      return
    if (spanRef.current.name !== view.name) {
      spanRef.current = { name: view.name, high: span.high }
      const y = frqRuler + (span.high - span.center) * frqRow + frqRow / 2
      const align = () => {
        yScroll.scrollTop = Math.max(0, y - yScroll.clientHeight / 2)
        if (xScroll)
          xScroll.scrollLeft = 0
        scrollXRef.current = 0
        setScrollX(0)
        setScrollY(yScroll.scrollTop)
      }
      align()
      requestAnimationFrame(align)
      return
    }
    const grown = span.high - spanRef.current.high
    spanRef.current.high = span.high
    if (grown === 0)
      return
    yScroll.scrollTop += grown * frqRow
    setScrollY(yScroll.scrollTop)
  }, [frequency.length, span.center, span.high, view?.name])

  useEffect(() => {
    const yScroll = yScrollRef.current
    const xScroll = xScrollRef.current
    if (!yScroll || !xScroll)
      return
    const sync = () => {
      const pad = Math.max(0, xScroll.clientWidth - yScroll.clientWidth)
      setTrackWidth(width + pad)
    }
    sync()
    const observer = new ResizeObserver(sync)
    observer.observe(yScroll)
    observer.observe(xScroll)
    return () => observer.disconnect()
  }, [width])

  useLayoutEffect(() => {
    const canvas = pianoRef.current
    if (!canvas || frequency.length === 0)
      return
    const ratio = bitmapRatio()
    canvas.width = Math.floor(width * ratio)
    canvas.height = Math.floor(pianoHeight * ratio)
    const ctx = canvasContext(canvas)
    if (!ctx)
      return
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0)
    const dark = document.documentElement.dataset.theme === "dark"
    ctx.fillStyle = dark ? "#161616" : "#f4f4f5"
    ctx.fillRect(0, 0, width, pianoHeight)
    const chosen = new Set(selected)
    for (let midi = span.low; midi <= span.high; midi++) {
      const y = frqRuler + (span.high - midi) * frqRow
      ctx.fillStyle = keyColor(midi, dark)
      ctx.fillRect(0, y, width, frqRow)
    }
    const accent = getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#0f766e"
    frequency.forEach((hz, frame) => {
      if (!(hz > 0) || !Number.isFinite(hz))
        return
      const x = frame * pixels
      const y = frqRuler + (span.high - midiOf(hz)) * frqRow
      ctx.fillStyle = chosen.has(frame) ? accent : "#fb923c"
      ctx.fillRect(x, y + 4, Math.max(2, pixels - 1), 6)
    })
  }, [appearance, frequency, pianoHeight, pixels, selected, span.high, span.low, width])

  useLayoutEffect(() => {
    const canvas = laneRef.current
    if (!canvas || frequency.length === 0)
      return
    const ratio = bitmapRatio()
    canvas.width = Math.floor(width * ratio)
    canvas.height = Math.floor(frqUnvoiced * ratio)
    const ctx = canvasContext(canvas)
    if (!ctx)
      return
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0)
    const dark = document.documentElement.dataset.theme === "dark"
    ctx.fillStyle = dark ? "#111827" : "#f3f4f6"
    ctx.fillRect(0, 0, width, frqUnvoiced)
    const chosen = new Set(selected)
    const accent = getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#0f766e"
    frequency.forEach((hz, frame) => {
      if (hz > 0 && Number.isFinite(hz))
        return
      ctx.fillStyle = chosen.has(frame) ? accent : "#9ca3af"
      ctx.fillRect(frame * pixels, (frqUnvoiced - 6) / 2, Math.max(2, pixels - 1), 6)
    })
  }, [appearance, frequency, pixels, selected, width])

  function at(clientX: number, clientY: number) {
    const yScroll = yScrollRef.current
    const lane = laneBoxRef.current
    if (!yScroll || frequency.length === 0)
      return { frame: 0, midi: span.low, unvoiced: false }
    const frame = localFrame(yScroll, clientX, scrollXRef.current, pixels, frequency.length)
    if (lane) {
      const box = lane.getBoundingClientRect()
      if (clientY >= box.top && clientY <= box.bottom)
        return { frame, midi: 0, unvoiced: true }
    }
    const rect = yScroll.getBoundingClientRect()
    const y = clientY - rect.top + yScroll.scrollTop
    const midi = Math.max(span.low, Math.min(span.high, span.high - (y - frqRuler) / frqRow))
    return { frame, midi, unvoiced: false }
  }

  function framesIn(x0: number, y0: number, x1: number, y1: number) {
    const yScroll = yScrollRef.current
    if (!yScroll)
      return []
    const yRect = yScroll.getBoundingClientRect()
    const lane = laneBoxRef.current?.getBoundingClientRect()
    const left = Math.min(x0, x1)
    const right = Math.max(x0, x1)
    const top = Math.min(y0, y1)
    const bottom = Math.max(y0, y1)
    const frames: number[] = []
    frequency.forEach((hz, frame) => {
      const x = yRect.left - scrollXRef.current + (frame + 0.5) * pixels
      const voiced = hz > 0 && Number.isFinite(hz)
      const y = voiced
        ? yRect.top - yScroll.scrollTop + frqRuler + (span.high - midiOf(hz) + 0.5) * frqRow
        : lane
          ? (lane.top + lane.bottom) / 2
          : -1
      if (x >= left && x <= right && y >= top && y <= bottom)
        frames.push(frame)
    })
    return frames
  }

  function onPointerDown(event: ReactPointerEvent<HTMLDivElement>) {
    if (event.button !== 0 || event.target === xScrollRef.current || event.target === yScrollRef.current)
      return
    const point = at(event.clientX, event.clientY)
    dragRef.current = { x: event.clientX, y: event.clientY, points: [point] }
    event.currentTarget.setPointerCapture(event.pointerId)
  }

  function onPointerMove(event: ReactPointerEvent<HTMLDivElement>) {
    const drag = dragRef.current
    if (!drag || !pen)
      return
    const point = at(event.clientX, event.clientY)
    const last = drag.points[drag.points.length - 1]
    if (!last || last.frame !== point.frame || last.unvoiced !== point.unvoiced || Math.abs(last.midi - point.midi) > 0.01)
      drag.points.push(point)
  }

  function onPointerUp(event: ReactPointerEvent<HTMLDivElement>) {
    const drag = dragRef.current
    dragRef.current = null
    if (!drag)
      return
    if (pen) {
      send({ type: "frq", op: "stroke", points: drag.points })
      return
    }
    setSelected(framesIn(drag.x, drag.y, event.clientX, event.clientY))
  }

  const message = !view
    ? ""
    : view.error
      ? view.error
      : view.missing
        ? "このファイルの .frq はありません。"
        : ""
  const dark = document.documentElement.dataset.theme === "dark"
  const laneColor = dark ? "#111827" : "#f3f4f6"

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
      {message && <p className="p-3 text-[var(--muted)]">{message}</p>}
      {frequency.length > 0 && (
        <div
          className={pen ? "frq-pen relative flex min-h-0 flex-1 flex-col touch-none" : "frq-select relative flex min-h-0 flex-1 flex-col touch-none"}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
        >
          <div className="relative min-h-0 flex-1">
            <div
              ref={yScrollRef}
              className="h-full overflow-x-hidden overflow-y-auto"
              onScroll={(event) => setScrollY(event.currentTarget.scrollTop)}
            >
              <div style={{ width, height: pianoHeight, transform: `translateX(${-scrollX}px)` }}>
                <canvas ref={bindPiano} style={{ width, height: pianoHeight }} />
              </div>
            </div>
            <div className="pointer-events-none absolute inset-0 overflow-hidden">
              <div className="relative" style={{ height: pianoHeight, transform: `translateY(${-scrollY}px)` }}>
                {cNotes.map((midi) => (
                  <span
                    key={midi}
                    className="absolute left-1.5 text-[11px] leading-[14px] text-[var(--text)]"
                    style={{ top: frqRuler + (span.high - midi) * frqRow }}
                  >
                    {noteName(midi)}
                  </span>
                ))}
              </div>
            </div>
          </div>
          <div ref={laneBoxRef} className="relative shrink-0 overflow-hidden border-t border-[var(--line)]" style={{ height: frqUnvoiced, background: laneColor }}>
            <canvas
              ref={bindLane}
              className="absolute top-0 left-0"
              style={{ width, height: frqUnvoiced, transform: `translateX(${-scrollX}px)` }}
            />
            <span
              className="pointer-events-none absolute inset-y-0 left-0 z-10 flex items-center px-2 text-[11px] text-[var(--text)]"
              style={{ background: laneColor }}
            >
              なし
            </span>
          </div>
          <div
            ref={xScrollRef}
            className="shrink-0 overflow-x-auto overflow-y-hidden"
            onScroll={(event) => {
              scrollXRef.current = event.currentTarget.scrollLeft
              setScrollX(event.currentTarget.scrollLeft)
            }}
          >
            <div style={{ width: trackWidth, height: 1 }} />
          </div>
        </div>
      )}
    </div>
  )
}
