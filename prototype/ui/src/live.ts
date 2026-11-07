import type { CaptureEvent } from "./protocol"

export type LiveBus = {
  show: (event: CaptureEvent) => void
  listen: (listener: (event: CaptureEvent) => void) => () => void
}

export function createLiveBus(): LiveBus {
  let current: CaptureEvent | null = null
  const listeners = new Set<(event: CaptureEvent) => void>()
  return {
    show(event) {
      current = event
      for (const listener of listeners)
        listener(event)
    },
    listen(listener) {
      listeners.add(listener)
      if (current)
        listener(current)
      return () => listeners.delete(listener)
    },
  }
}
