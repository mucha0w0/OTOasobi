export type PointerBus = {
  set: (time: number | null) => void
  get: () => number | null
  watch: (apply: (time: number | null) => void) => () => void
}

export function createPointerBus(): PointerBus {
  let time: number | null = null
  const listeners = new Set<(value: number | null) => void>()
  return {
    set(next) {
      time = next
      for (const listener of listeners)
        listener(time)
    },
    get: () => time,
    watch(apply) {
      listeners.add(apply)
      apply(time)
      return () => listeners.delete(apply)
    },
  }
}

export function niceStep(span: number, width: number): number {
  const raw = span / Math.max(1, width / 80)
  if (!(raw > 0) || !Number.isFinite(raw))
    return 1
  const pow = 10 ** Math.floor(Math.log10(raw))
  const n = raw / pow
  const mark = n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10
  return mark * pow
}

export function formatTick(time: number, step: number): string {
  const digits = step >= 1 ? 0 : 3
  return `${time.toFixed(digits)} s`
}

export function rulerTicks(t0: number, t1: number, width: number): { time: number; label: string }[] {
  const span = t1 - t0
  if (!(span > 0) || !(width > 0))
    return []
  const step = niceStep(span, width)
  const start = Math.ceil((t0 - step * 1e-9) / step) * step
  const count = Math.floor((t1 - start) / step + 1e-6)
  const ticks: { time: number; label: string }[] = []
  for (let index = 0; index <= count; index++) {
    const time = start + index * step
    const x = ((time - t0) / span) * width
    if (x < 36 || x > width - 36)
      continue
    ticks.push({ time, label: formatTick(time, step) })
  }
  return ticks
}

export function xForTime(time: number, t0: number, t1: number, width: number): number | null {
  const span = t1 - t0
  if (!(span > 0) || !(width > 0))
    return null
  const x = ((time - t0) / span) * width
  if (x < 0 || x > width)
    return null
  return x
}
