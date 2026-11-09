import { useEffect, useLayoutEffect, useRef, useState, type PointerEvent } from "react"
import { bitmapRatio, canvasContext, shouldReplaceBitmap } from "./bitmap"
import {
  clampNoiseView,
  noiseMaxZoom,
  noisePlotHeight,
  samplePeaks,
  shadeRect,
  timeAtNoise,
  zoomNoiseView,
  type NoiseView,
} from "./noiseWave"
import type { NoiseWaveEvent } from "./protocol"

export function NoiseWaveform({
  wave,
  start,
  end,
  appearance,
  onChanging,
  onChosen,
}: {
  wave: NoiseWaveEvent
  start: number | null
  end: number | null
  appearance: string
  onChanging: (start: number, end: number) => void
  onChosen: (start: number, end: number) => void
}) {
  const frameRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const barRef = useRef<HTMLDivElement>(null)
  const innerRef = useRef<HTMLDivElement>(null)
  const viewRef = useRef<NoiseView>({ start: wave.viewStart, length: wave.viewLength })
  const barSync = useRef(false)
  const drag = useRef<{ anchor: number; pointer: number } | null>(null)
  const [view, setView] = useState<NoiseView>(() => clampNoiseView(wave.viewStart, wave.viewLength, wave.duration))
  viewRef.current = view

  useEffect(() => {
    const next = clampNoiseView(wave.viewStart, wave.viewLength || wave.duration, wave.duration)
    viewRef.current = next
    setView(next)
  }, [wave.file, wave.duration, wave.viewStart, wave.viewLength])

  useEffect(() => {
    const canvas = canvasRef.current
    const frame = frameRef.current
    if (!canvas || !frame)
      return
    const draw = () => {
      const width = frame.clientWidth
      const height = noisePlotHeight
      if (width < 2)
        return
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
      const ink = css("--wave")
      const muted = css("--muted")
      const accent = css("--accent")
      const shown = viewRef.current
      const columns = Math.max(1, Math.floor(width))
      const mid = height / 2
      ctx.beginPath()
      const top: { x: number; y: number }[] = []
      const bottom: { x: number; y: number }[] = []
      for (let column = 0; column < columns; column++) {
        const t0 = shown.start + shown.length * column / columns
        const t1 = shown.start + shown.length * (column + 1) / columns
        const peak = samplePeaks(wave.lo, wave.hi, wave.duration, t0, t1)
        const x = column + 0.5
        top.push({ x, y: mid - peak.max * (mid - 2) })
        bottom.push({ x, y: mid - peak.min * (mid - 2) })
      }
      if (top[0]) {
        ctx.moveTo(top[0].x, top[0].y)
        for (let index = 1; index < top.length; index++)
          ctx.lineTo(top[index]!.x, top[index]!.y)
        for (let index = bottom.length - 1; index >= 0; index--)
          ctx.lineTo(bottom[index]!.x, bottom[index]!.y)
        ctx.closePath()
        ctx.fillStyle = ink
        ctx.strokeStyle = ink
        ctx.lineWidth = 1
        ctx.fill()
        ctx.stroke()
      }
      ctx.globalAlpha = 0.45
      ctx.fillStyle = muted
      ctx.fillRect(0, mid, width, 1)
      ctx.globalAlpha = 1
      if (start != null && end != null) {
        const shade = shadeRect(start, end, shown, width, height)
        if (shade) {
          ctx.globalAlpha = 0.32
          ctx.fillStyle = accent
          ctx.fillRect(shade.x, shade.y, shade.width, shade.height)
          ctx.globalAlpha = 1
        }
      }
    }
    const observer = new ResizeObserver(draw)
    observer.observe(frame)
    draw()
    return () => observer.disconnect()
  }, [wave, start, end, appearance, view])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas)
      return
    const wheel = (event: WheelEvent) => {
      event.preventDefault()
      event.stopPropagation()
      if (event.deltaY === 0 || !(wave.duration > 0))
        return
      const rect = canvas.getBoundingClientRect()
      const center = timeAtNoise(event.clientX - rect.left, rect.width, viewRef.current, wave.duration)
      setView(zoomNoiseView(viewRef.current, center, event.deltaY < 0 ? 1 / 1.25 : 1.25, wave.duration))
    }
    canvas.addEventListener("wheel", wheel, { passive: false })
    return () => canvas.removeEventListener("wheel", wheel)
  }, [wave.duration])

  useLayoutEffect(() => {
    const bar = barRef.current
    const inner = innerRef.current
    if (!bar || !inner)
      return
    const span = Math.max(0, wave.duration - view.length)
    const width = bar.clientWidth
    inner.style.width = span <= 0 || width <= 0 ? "100%" : `${Math.min(width * noiseMaxZoom, Math.max(width + 1, width * wave.duration / view.length))}px`
    barSync.current = true
    bar.scrollLeft = span <= 0 || view.length <= 0 ? 0 : view.start * bar.clientWidth / view.length
    barSync.current = false
  }, [wave.duration, view])

  function timeFromEvent(event: PointerEvent<HTMLCanvasElement>) {
    const rect = event.currentTarget.getBoundingClientRect()
    return timeAtNoise(event.clientX - rect.left, rect.width, viewRef.current, wave.duration)
  }

  return (
    <div className="min-w-0 overflow-hidden">
      <div ref={frameRef} className="overflow-hidden rounded bg-[var(--canvas)]" style={{ height: noisePlotHeight }}>
        <canvas
          ref={canvasRef}
          className="h-full w-full touch-none select-none"
          aria-label="無音の範囲"
          onPointerDown={(event) => {
            if (event.button !== 0 || !(wave.duration > 0))
              return
            const time = timeFromEvent(event)
            drag.current = { anchor: time, pointer: event.pointerId }
            event.currentTarget.setPointerCapture(event.pointerId)
            onChanging(time, time)
          }}
          onPointerMove={(event) => {
            const held = drag.current
            if (!held || held.pointer !== event.pointerId)
              return
            onChanging(held.anchor, timeFromEvent(event))
          }}
          onPointerUp={(event) => {
            const held = drag.current
            if (!held || held.pointer !== event.pointerId)
              return
            drag.current = null
            if (event.currentTarget.hasPointerCapture(event.pointerId))
              event.currentTarget.releasePointerCapture(event.pointerId)
            onChosen(held.anchor, timeFromEvent(event))
          }}
          onPointerCancel={(event) => {
            const held = drag.current
            if (!held || held.pointer !== event.pointerId)
              return
            drag.current = null
            onChosen(held.anchor, timeFromEvent(event))
          }}
        />
      </div>
      <div
        ref={barRef}
        className="mt-1 h-[18px] w-full min-w-0 overflow-x-auto overflow-y-hidden"
        onScroll={(event) => {
          if (barSync.current || view.length <= 0)
            return
          const left = event.currentTarget.scrollLeft
          setView(clampNoiseView(left * view.length / Math.max(event.currentTarget.clientWidth, 1), view.length, wave.duration))
        }}
      >
        <div ref={innerRef} className="h-px" />
      </div>
    </div>
  )
}

function css(name: string) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || "#222"
}
