import { parseLayout, type LayoutNode } from "./layout"

export type PreviewGesture = "boundary" | "pan" | "zoom" | "resize"

export type PageCommand =
  | { type: "hello" }
  | { type: "openWav" }
  | { type: "openFolder" }
  | { type: "selectFile"; name: string }
  | { type: "preview"; t0: number; t1: number; columns: number; rows: number }
  | { type: "setDevices"; inputId: string; outputId: string }
  | { type: "monitor"; on: boolean }
  | { type: "guide"; note: string; semitones: number; speed: number }
  | { type: "guideStop" }
  | { type: "record" }
  | { type: "recordStop" }
  | { type: "watchMic"; on: boolean }
  | { type: "close" }
  | { type: "caption"; theme: "light" | "dark" }
  | { type: "layout"; root: LayoutNode | null }
  | { type: "oto"; op: "mode"; mode: string }
  | { type: "oto"; op: "slider"; group: string; key: string; value: number }
  | { type: "oto"; op: "preset"; target: string; name: string }
  | { type: "oto"; op: "savePreset"; which: string; name: string }
  | { type: "oto"; op: "alias"; which: string; on: boolean }
  | { type: "oto"; op: "scope"; scope: string }
  | { type: "oto"; op: "analyze"; force: boolean }
  | { type: "oto"; op: "write" }
  | { type: "saveSettings"; name: string; replace: boolean }
  | { type: "loadSettings"; name: string }

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
  loudness: number[]
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
  | { type: "files"; folder: string; names: string[] }
  | { type: "opened"; name: string }
  | PreviewEvent
  | CaptureEvent
  | { type: "guide"; playing: boolean }
  | SavedEvent
  | { type: "failed"; message: string }
  | { type: "notice"; message: string }
  | { type: "otoExists"; name: string }
  | { type: "layout"; root: LayoutNode | null }
  | OtoState

export type OtoMark = {
  alias: string
  offset: number
  overlap: number
  preutter: number
  consonant: number
  blank: number
}

export type OtoKind = { group: string; key: string; caption: string }

export type OtoState = {
  type: "oto"
  mode: "mono" | "renzoku" | "cvvc"
  busy: boolean
  analyzed: boolean
  hasIni: boolean
  scope: "all" | "missing" | "overlap"
  aliasMono: boolean
  aliasMonoLocked: boolean
  aliasRenzoku: boolean
  aliasCvvc: boolean
  presets: string[]
  cvvcPresets: string[]
  saves: { name: string; mode: string }[]
  standalonePreset: string
  standaloneDirty: boolean
  restPreset: string
  leadPreset: string
  cvvcPreset: string
  cvvcDirty: boolean
  offset: number
  consonant: number
  blank: number
  overlap: Record<string, number>
  renzokuOffset: number
  renzokuConsonant: number
  renzokuBlank: number
  renzokuOverlap: number
  cvvcConsonant: number
  cvvcBlank: number
  cvvcOverlap: Record<string, number>
  vcOffset: number
  vcOverlap: number
  vcConsonant: Record<string, number>
  vcBlank: Record<string, number>
  kinds: OtoKind[]
  marks: OtoMark[]
}

export function requestsPreview(gesture: PreviewGesture): boolean {
  return gesture === "pan"
}

export function requestsPreviewAfter(gesture: PreviewGesture): boolean {
  return gesture === "pan" || gesture === "zoom" || gesture === "resize"
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
    case "files":
      if (typeof value.folder !== "string" || !Array.isArray(value.names))
        return null
      return {
        type: "files",
        folder: value.folder,
        names: value.names.filter((item): item is string => typeof item === "string"),
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
        loudness: Array.isArray(value.loudness)
          ? value.loudness.filter((item): item is number => typeof item === "number")
          : [],
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
    case "notice":
      return typeof value.message === "string" ? { type: "notice", message: value.message } : null
    case "otoExists":
      return typeof value.name === "string" ? { type: "otoExists", name: value.name } : null
    case "oto":
      return parseOto(value)
    case "layout": {
      if (value.root === null)
        return { type: "layout", root: null }
      const root = parseLayout(value.root)
      return root ? { type: "layout", root } : null
    }
    default:
      return null
  }
}

function parseOto(value: Record<string, unknown>): OtoState | null {
  if (value.mode !== "mono" && value.mode !== "renzoku" && value.mode !== "cvvc")
    return null
  if (value.scope !== "all" && value.scope !== "missing" && value.scope !== "overlap")
    return null
  return {
    type: "oto",
    mode: value.mode,
    busy: value.busy === true,
    analyzed: value.analyzed === true,
    hasIni: value.hasIni === true,
    scope: value.scope,
    aliasMono: value.aliasMono === true,
    aliasMonoLocked: value.aliasMonoLocked === true,
    aliasRenzoku: value.aliasRenzoku === true,
    aliasCvvc: value.aliasCvvc === true,
    presets: strings(value.presets),
    cvvcPresets: strings(value.cvvcPresets),
    saves: Array.isArray(value.saves)
      ? value.saves.flatMap((item) => {
          if (!isRecord(item) || typeof item.name !== "string" || typeof item.mode !== "string")
            return []
          return [{ name: item.name, mode: item.mode }]
        })
      : [],
    standalonePreset: typeof value.standalonePreset === "string" ? value.standalonePreset : "初期値",
    standaloneDirty: value.standaloneDirty === true,
    restPreset: typeof value.restPreset === "string" ? value.restPreset : "初期値",
    leadPreset: typeof value.leadPreset === "string" ? value.leadPreset : "初期値",
    cvvcPreset: typeof value.cvvcPreset === "string" ? value.cvvcPreset : "初期値",
    cvvcDirty: value.cvvcDirty === true,
    offset: numberOr(value.offset, 0),
    consonant: numberOr(value.consonant, 0.2),
    blank: numberOr(value.blank, 0),
    overlap: numberMap(value.overlap),
    renzokuOffset: numberOr(value.renzokuOffset, 0),
    renzokuConsonant: numberOr(value.renzokuConsonant, 0.2),
    renzokuBlank: numberOr(value.renzokuBlank, 0),
    renzokuOverlap: numberOr(value.renzokuOverlap, 50),
    cvvcConsonant: numberOr(value.cvvcConsonant, 0.2),
    cvvcBlank: numberOr(value.cvvcBlank, 0),
    cvvcOverlap: numberMap(value.cvvcOverlap),
    vcOffset: numberOr(value.vcOffset, 0),
    vcOverlap: numberOr(value.vcOverlap, 50),
    vcConsonant: numberMap(value.vcConsonant),
    vcBlank: numberMap(value.vcBlank),
    kinds: Array.isArray(value.kinds)
      ? value.kinds.flatMap((item) => {
          if (!isRecord(item) || typeof item.group !== "string" || typeof item.key !== "string" || typeof item.caption !== "string")
            return []
          return [{ group: item.group, key: item.key, caption: item.caption }]
        })
      : [],
    marks: Array.isArray(value.marks)
      ? value.marks.flatMap((item) => {
          if (!isRecord(item) || typeof item.alias !== "string")
            return []
          return [{
            alias: item.alias,
            offset: numberOr(item.offset, 0),
            overlap: numberOr(item.overlap, 0),
            preutter: numberOr(item.preutter, 0),
            consonant: numberOr(item.consonant, 0),
            blank: numberOr(item.blank, 0),
          }]
        })
      : [],
  }
}

function strings(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : []
}

function numberMap(value: unknown): Record<string, number> {
  if (!isRecord(value))
    return {}
  const result: Record<string, number> = {}
  for (const [key, item] of Object.entries(value)) {
    if (typeof item === "number" && Number.isFinite(item))
      result[key] = item
  }
  return result
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
