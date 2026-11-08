export type Range = { t0: number; t1: number }

export function zoomRange(range: Range, anchor: number, factor: number, duration: number): Range {
  const current = Math.max(1e-12, range.t1 - range.t0)
  const limit = duration > 0 ? duration : current
  const span = Math.min(Math.max(current * factor, 0.01), limit)
  const ratio = (anchor - range.t0) / current
  return clampRange(anchor - span * ratio, span, duration)
}

export function panRange(range: Range, fraction: number, duration: number): Range {
  const span = Math.max(1e-12, range.t1 - range.t0)
  return clampRange(range.t0 + span * fraction, span, duration)
}

const loudnessRangeDb = 40

export function loudnessX(
  index: number,
  count: number,
  duration: number,
  view: Range,
  width: number,
): number {
  const shown = view.t1 > view.t0 ? view : { t0: 0, t1: duration }
  const span = shown.t1 - shown.t0
  if (count < 1 || !(duration > 0) || !(span > 0) || !(width > 0))
    return 0
  const time = duration * (index + 0.5) / count
  return ((time - shown.t0) / span) * width
}

export function loudnessLevel(value: number, peak: number): number {
  return Math.min(1, Math.max(0, (value - (peak - loudnessRangeDb)) / loudnessRangeDb))
}

export function imagePlacement(
  source: Range,
  view: Range,
  width: number,
): { x: number; width: number } {
  const span = view.t1 - view.t0
  const sourceSpan = source.t1 - source.t0
  if (!(span > 0) || !(sourceSpan > 0) || !(width > 0))
    return { x: 0, width }
  return {
    x: ((source.t0 - view.t0) / span) * width,
    width: (sourceSpan / span) * width,
  }
}

function clampRange(start: number, span: number, duration: number): Range {
  if (!(duration > 0))
    return { t0: start, t1: start + span }
  const width = Math.min(span, duration)
  const t0 = Math.min(Math.max(0, start), duration - width)
  return { t0, t1: t0 + width }
}
