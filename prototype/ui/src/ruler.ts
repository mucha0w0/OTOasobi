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

export function xForTime(time: number, t0: number, t1: number, width: number): number | null {
  const span = t1 - t0
  if (!(span > 0) || !(width > 0))
    return null
  const x = ((time - t0) / span) * width
  if (x < 0 || x > width)
    return null
  return x
}
