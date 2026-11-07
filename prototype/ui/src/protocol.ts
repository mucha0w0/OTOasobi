export type PreviewGesture = "boundary" | "pan" | "zoom" | "resize"

export type PageCommand =
  | { type: "hello" }
  | { type: "openWav" }
  | { type: "preview"; t0: number; t1: number; columns: number; rows: number }
  | { type: "setDevices"; inputId: string; outputId: string }
  | { type: "monitor"; on: boolean }
  | { type: "guide"; note: string; semitones: number; speed: number }
  | { type: "guideStop" }
  | { type: "record" }
  | { type: "recordStop" }
  | { type: "caption"; theme: "light" | "dark" }

export type DeviceInfo = { id: string; label: string }

export type PreviewEvent = {
  type: "preview"
  duration: number
  lo: number[]
  hi: number[]
  png: string
  specT0: number
  specT1: number
  waveT0: number
  waveT1: number
  sampleRate: number
}

export type CaptureEvent = {
  type: "capture"
  running: boolean
  recording: boolean
  rate: number
  peak: number
  lo: number[]
  hi: number[]
  listenError: string | null
}

export type SavedEvent = {
  type: "saved"
  path: string
  inputRate: number
  headerRate: number
  bits: number
  channels: number
  seconds: number
  elapsed: number
}

export type HostEvent =
  | { type: "devices"; inputs: DeviceInfo[]; outputs: DeviceInfo[] }
  | { type: "opened"; name: string }
  | PreviewEvent
  | CaptureEvent
  | { type: "guide"; playing: boolean }
  | SavedEvent
  | { type: "failed"; message: string }

export function requestsPreview(gesture: PreviewGesture): boolean {
  return gesture === "pan" || gesture === "zoom"
}

export function timeAt(x: number, width: number, t0: number, t1: number, panPx = 0): number {
  if (!(width > 0))
    return t0
  return t0 + ((x - panPx) / width) * (t1 - t0)
}

export function xAt(time: number, width: number, t0: number, t1: number, panPx = 0): number {
  const span = t1 - t0
  if (!(span > 0) || !(width > 0))
    return panPx
  return ((time - t0) / span) * width + panPx
}

export function shownInputRate(capture: { rate: number } | null): number | null {
  if (!capture || !(capture.rate > 0))
    return null
  return capture.rate
}

export function parseHostEvent(value: unknown): HostEvent | null {
  if (!isRecord(value) || typeof value.type !== "string")
    return null
  switch (value.type) {
    case "devices":
      if (!Array.isArray(value.inputs) || !Array.isArray(value.outputs))
        return null
      return {
        type: "devices",
        inputs: value.inputs.filter(isDevice),
        outputs: value.outputs.filter(isDevice),
      }
    case "opened":
      return typeof value.name === "string" ? { type: "opened", name: value.name } : null
    case "preview":
      if (!Array.isArray(value.lo) || !Array.isArray(value.hi) || typeof value.png !== "string")
        return null
      return {
        type: "preview",
        duration: numberOr(value.duration, 0),
        lo: value.lo.filter((item): item is number => typeof item === "number"),
        hi: value.hi.filter((item): item is number => typeof item === "number"),
        png: value.png,
        specT0: numberOr(value.specT0, 0),
        specT1: numberOr(value.specT1, 0),
        waveT0: numberOr(value.waveT0, 0),
        waveT1: numberOr(value.waveT1, 0),
        sampleRate: numberOr(value.sampleRate, 0),
      }
    case "capture":
      if (!Array.isArray(value.lo) || !Array.isArray(value.hi))
        return null
      return {
        type: "capture",
        running: value.running === true,
        recording: value.recording === true,
        rate: numberOr(value.rate, 0),
        peak: numberOr(value.peak, 0),
        lo: value.lo.filter((item): item is number => typeof item === "number"),
        hi: value.hi.filter((item): item is number => typeof item === "number"),
        listenError: typeof value.listenError === "string" ? value.listenError : null,
      }
    case "guide":
      return { type: "guide", playing: value.playing === true }
    case "saved":
      if (typeof value.path !== "string")
        return null
      return {
        type: "saved",
        path: value.path,
        inputRate: numberOr(value.inputRate, 0),
        headerRate: numberOr(value.headerRate, 0),
        bits: numberOr(value.bits, 0),
        channels: numberOr(value.channels, 0),
        seconds: numberOr(value.seconds, 0),
        elapsed: numberOr(value.elapsed, 0),
      }
    case "failed":
      return typeof value.message === "string" ? { type: "failed", message: value.message } : null
    default:
      return null
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null
}

function isDevice(value: unknown): value is DeviceInfo {
  return isRecord(value) && typeof value.id === "string" && typeof value.label === "string"
}

function numberOr(value: unknown, fallback: number): number {
  return typeof value === "number" && Number.isFinite(value) ? value : fallback
}
