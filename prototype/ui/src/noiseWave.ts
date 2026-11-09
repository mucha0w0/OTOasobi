export const noisePlotHeight = 88
export const noiseMinView = 0.12
export const noiseMaxZoom = 16
export const noiseOpenView = 2
export const noisePeakFloor = 1e-3

export type NoiseView = { start: number; length: number }

export function minNoiseView(duration: number) {
  if (!(duration > 0))
    return noiseMinView
  return Math.min(duration, Math.max(noiseMinView, duration / noiseMaxZoom))
}

export function clampNoiseView(start: number, length: number, duration: number): NoiseView {
  if (!(duration > 0))
    return { start: 0, length: 1 }
  const floor = minNoiseView(duration)
  const span = Math.min(Math.max(length, floor), duration)
  if (span >= duration)
    return { start: 0, length: duration }
  return { start: Math.min(Math.max(0, start), duration - span), length: span }
}

export function zoomNoiseView(view: NoiseView, center: number, factor: number, duration: number): NoiseView {
  if (!(duration > 0))
    return view
  const next = clampNoiseView(0, view.length * factor, duration)
  const anchor = view.length <= 0 ? 0 : (center - view.start) / view.length
  return clampNoiseView(center - anchor * next.length, next.length, duration)
}

export function timeAtNoise(x: number, width: number, view: NoiseView, duration: number) {
  const span = Math.max(width, 1)
  return Math.min(Math.max(0, view.start + x / span * view.length), Math.max(0, duration))
}

export function shadeRect(start: number, end: number, view: NoiseView, width: number, height: number) {
  if (!(end > start) && !(start > end))
    return null
  const left = Math.min(start, end)
  const right = Math.max(start, end)
  const viewEnd = view.start + view.length
  const from = Math.max(left, view.start)
  const to = Math.min(right, viewEnd)
  if (!(to > from) || !(view.length > 0) || !(width > 0))
    return null
  const x = (from - view.start) / view.length * width
  const x1 = (to - view.start) / view.length * width
  return { x, y: 0, width: Math.max(1, x1 - x), height }
}

export function samplePeaks(lo: number[], hi: number[], duration: number, t0: number, t1: number) {
  const count = Math.min(lo.length, hi.length)
  if (count === 0 || !(duration > 0))
    return { min: 0, max: 0 }
  const i0 = Math.max(0, Math.min(count - 1, Math.floor(t0 / duration * count)))
  const i1 = Math.max(i0 + 1, Math.min(count, Math.ceil(t1 / duration * count)))
  let min = lo[i0] ?? 0
  let max = hi[i0] ?? 0
  for (let index = i0; index < i1; index++) {
    min = Math.min(min, lo[index] ?? 0)
    max = Math.max(max, hi[index] ?? 0)
  }
  return { min: Math.min(Math.max(min, -1), 1), max: Math.min(Math.max(max, -1), 1) }
}

export function formatNoiseSeconds(start: number, end: number) {
  return `${Math.abs(end - start).toFixed(2)}秒`
}

export function wavePeak(min: number, max: number, floor = noisePeakFloor) {
  return Math.max(Math.abs(min), Math.abs(max), floor)
}

export function filePeak(lo: number[], hi: number[]) {
  const count = Math.min(lo.length, hi.length)
  let min = 0
  let max = 0
  for (let index = 0; index < count; index++) {
    min = Math.min(min, lo[index] ?? 0)
    max = Math.max(max, hi[index] ?? 0)
  }
  return wavePeak(min, max)
}
