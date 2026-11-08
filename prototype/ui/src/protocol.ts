import { parseLayout, type LayoutNode } from "./layout"
import type { PhoneSpan } from "./spans"

export type PreviewGesture = "boundary" | "pan" | "zoom" | "resize"

export type PageCommand =
  | { type: "hello" }
  | { type: "openWav" }
  | { type: "openFolder" }
  | { type: "pickProjectPlace" }
  | { type: "createProject"; parent: string; name: string }
  | { type: "selectFile"; name: string }
  | { type: "preview"; t0: number; t1: number; columns: number; rows: number }
  | { type: "setDevices"; inputId: string; outputId: string }
  | { type: "monitor"; on: boolean }
  | { type: "guide"; note: string; semitones: number; speed: number; volume?: number }
  | { type: "guideStop" }
  | { type: "record"; name?: string; countIn?: boolean; bpm?: number; guideWhile?: boolean; volume?: number; note?: string; semitones?: number; speed?: number }
  | { type: "recordStop" }
  | { type: "recSettings"; note: string; semitones: number; speed: number; volume: number; countIn: boolean; bpm: number; guideWhile: boolean; advance: boolean }
  | { type: "recList"; op: "none" | "pick" | "open"; path?: string }
  | { type: "frq"; op: "write"; mode: "all" | "missing" | "selected"; clear: boolean }
  | { type: "frq"; op: "stroke"; points: { frame: number; midi: number; unvoiced: boolean }[] }
  | { type: "frq"; op: "octave"; frames: number[]; octaves: number }
  | { type: "frq"; op: "clear"; frames: number[] }
  | { type: "bank"; generation: number; name: string; image: string; sample: string; author: string; web: string; profile: BankRow[]; extra: string; readme: string; voice: string; version: string; locales: BankLocale[]; portrait: string; opacity: number; height: number }
  | { type: "bankPick"; which: "icon" | "portrait" }
  | { type: "effects"; op: "set"; noiseEnabled: boolean; noiseReduction: number; highpassEnabled: boolean; highpassHz: number; highpassSlope: number; eqEnabled: boolean; gains: number[]; compressorEnabled: boolean; compressorRatio: number; compressorReduction: number; normalizeEnabled: boolean; normalizeTarget: number }
  | { type: "effects"; op: "preset"; name: string }
  | { type: "effects"; op: "noise"; file: string; start: number; end: number }
  | { type: "effects"; op: "apply" }
  | { type: "effects"; op: "move"; from: number; to: number }
  | { type: "watchMic"; on: boolean }
  | { type: "close" }
  | { type: "caption"; theme: "light" | "dark" }
  | { type: "layout"; root: LayoutNode | null; keepRatios?: boolean }
  | { type: "guideFile"; manual?: boolean; path?: string; pick?: boolean }
  | { type: "take"; op: "play" | "stop" | "wave"; name?: string }
  | { type: "openRecent"; path: string }
  | { type: "template"; op: "apply" | "save"; name: string }
  | { type: "appearance"; theme: "light" | "dark" | "system"; accent: string | null }
  | { type: "update"; op: "check" | "apply" }
  | { type: "oto"; op: "mode"; mode: string }
  | { type: "oto"; op: "slider"; group: string; key: string; value: number }
  | { type: "oto"; op: "preset"; target: string; name: string }
  | { type: "oto"; op: "savePreset"; which: string; name: string }
  | { type: "oto"; op: "alias"; which: string; on: boolean }
  | { type: "oto"; op: "scope"; scope: string }
  | { type: "oto"; op: "analyze"; force: boolean }
  | { type: "oto"; op: "analyzeFile" }
  | { type: "oto"; op: "write" }
  | { type: "oto"; op: "tune"; file: string; index: number; point: number; time: number }
  | { type: "oto"; op: "spans"; file: string; spans: { start: number; end: number }[] }
  | { type: "oto"; op: "fields"; file: string; index: number; alias: string; mode?: string; offset: number; overlap: number; preutter: number; consonant: number; blank: number }
  | { type: "oto"; op: "aliasName"; file: string; index: number; alias: string; mode?: string; name: string }
  | { type: "oto"; op: "comment"; file: string; index: number; alias: string; mode?: string; comment: string }
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
  | { type: "opened"; name: string; missing?: boolean }
  | { type: "status"; message: string }
  | { type: "recordWait"; waiting: boolean }
  | { type: "frq"; wavs: number; frqs: number; busy: boolean }
  | FrqViewEvent
  | RecListEvent
  | RecSettingsEvent
  | BankEvent
  | EffectsEvent
  | PreviewEvent
  | CaptureEvent
  | { type: "guide"; playing: boolean }
  | { type: "guideFile"; manual: boolean; path: string; label: string; recent: { path: string; label: string }[] }
  | { type: "take"; playing: boolean; position: number; duration: number }
  | { type: "takeWave"; name: string; lo: number[]; hi: number[]; duration: number }
  | { type: "recent"; folders: { path: string; name: string }[] }
  | { type: "projectPlace"; path: string }
  | { type: "templates"; names: string[] }
  | { type: "appearance"; theme: "light" | "dark" | "system"; accent: string | null }
  | { type: "update"; phase: "prompt" | "current" | "unavailable" | "progress" | "done" | "failed"; title: string; body: string }
  | SavedEvent
  | { type: "failed"; message: string }
  | { type: "notice"; message: string }
  | { type: "otoExists"; name: string }
  | { type: "layout"; root: LayoutNode | null; keepRatios: boolean }
  | OtoState

export type FrqViewEvent = {
  type: "frqView"
  missing: boolean
  name: string
  hop: number
  sampleRate: number
  frequency: number[]
  error: string | null
}

export type RecLineInfo = {
  sing: string
  comment: string
  error: string | null
  recorded: boolean
  wav: string
}

export type RecListEvent = {
  type: "recList"
  path: string
  recent: { path: string; label: string }[]
  lines: RecLineInfo[]
  outside: string[]
}

export type RecSettingsEvent = {
  type: "recSettings"
  note: string
  semitones: number
  speed: number
  volume: number
  countIn: boolean
  bpm: number
  guideWhile: boolean
  advance: boolean
}

export type BankRow = { label: string; value: string; fullwidth: boolean }

export type BankLocale = { code: string; name: string }

export type BankEvent = {
  type: "bank"
  generation: number
  name: string
  image: string
  sample: string
  samples: string[]
  author: string
  web: string
  profile: BankRow[]
  extra: string
  readme: string
  encoding: string
  voice: string
  version: string
  locales: BankLocale[]
  languages: { code: string; label: string }[]
  portrait: string
  opacity: number
  height: number
  icon: string | null
  portraitImage: string | null
}

export type EffectsEvent = {
  type: "effects"
  preset: string
  presets: string[]
  noiseEnabled: boolean
  noiseReduction: number
  noiseFiles: string[]
  noiseFile: string
  noiseStart: number
  noiseEnd: number
  noiseReady: boolean
  highpassEnabled: boolean
  highpassHz: number
  highpassSlope: number
  eqEnabled: boolean
  gains: number[]
  compressorEnabled: boolean
  compressorRatio: number
  compressorReduction: number
  normalizeEnabled: boolean
  normalizeTarget: number
  noiseNote: string | null
  order: string[]
}

export type OtoMark = {
  alias: string
  offset: number
  overlap: number
  preutter: number
  consonant: number
  blank: number
  file: string
  index: number
  autoAlias: string
  name: string
  comment: string
  tuned: boolean[]
  mode?: string
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
  rows: OtoMark[]
  spans: PhoneSpan[]
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
      return typeof value.name === "string" ? { type: "opened", name: value.name, missing: value.missing === true } : null
    case "status":
      return typeof value.message === "string" ? { type: "status", message: value.message } : null
    case "recordWait":
      return { type: "recordWait", waiting: value.waiting === true }
    case "frq":
      return { type: "frq", wavs: numberOr(value.wavs, 0), frqs: numberOr(value.frqs, 0), busy: value.busy === true }
    case "frqView":
      return {
        type: "frqView",
        missing: value.missing === true,
        name: typeof value.name === "string" ? value.name : "",
        hop: numberOr(value.hop, 0),
        sampleRate: numberOr(value.sampleRate, 0),
        frequency: Array.isArray(value.frequency)
          ? value.frequency.filter((item): item is number => typeof item === "number" && Number.isFinite(item))
          : [],
        error: typeof value.error === "string" ? value.error : null,
      }
    case "recList":
      return parseRecList(value)
    case "recSettings":
      return {
        type: "recSettings",
        note: typeof value.note === "string" ? value.note : "C4",
        semitones: numberOr(value.semitones, 0),
        speed: numberOr(value.speed, 1),
        volume: numberOr(value.volume, 0.7),
        countIn: value.countIn === true,
        bpm: numberOr(value.bpm, 120),
        guideWhile: value.guideWhile === true,
        advance: value.advance === true,
      }
    case "bank":
      return parseBank(value)
    case "effects":
      return parseEffects(value)
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
      const keepRatios = value.keepRatios === true
      if (value.root === null)
        return { type: "layout", root: null, keepRatios }
      const root = parseLayout(value.root)
      return root ? { type: "layout", root, keepRatios } : null
    }
    case "guideFile":
      return {
        type: "guideFile",
        manual: value.manual !== false,
        path: typeof value.path === "string" ? value.path : "",
        label: typeof value.label === "string" ? value.label : "",
        recent: Array.isArray(value.recent)
          ? value.recent.flatMap((item) => isRecord(item) && typeof item.path === "string"
            ? [{ path: item.path, label: typeof item.label === "string" ? item.label : item.path }]
            : [])
          : [],
      }
    case "take":
      return { type: "take", playing: value.playing === true, position: numberOr(value.position, 0), duration: numberOr(value.duration, 0) }
    case "takeWave":
      return {
        type: "takeWave",
        name: typeof value.name === "string" ? value.name : "",
        lo: Array.isArray(value.lo) ? value.lo.filter((item): item is number => typeof item === "number") : [],
        hi: Array.isArray(value.hi) ? value.hi.filter((item): item is number => typeof item === "number") : [],
        duration: numberOr(value.duration, 0),
      }
    case "recent":
      return {
        type: "recent",
        folders: Array.isArray(value.folders)
          ? value.folders.flatMap((item) => isRecord(item) && typeof item.path === "string"
            ? [{ path: item.path, name: typeof item.name === "string" ? item.name : item.path }]
            : [])
          : [],
      }
    case "projectPlace":
      return typeof value.path === "string" ? { type: "projectPlace", path: value.path } : null
    case "templates":
      return { type: "templates", names: strings(value.names) }
    case "appearance":
      return {
        type: "appearance",
        theme: value.theme === "light" || value.theme === "dark" ? value.theme : "system",
        accent: typeof value.accent === "string" ? value.accent : null,
      }
    case "update":
      return {
        type: "update",
        phase: value.phase === "prompt" || value.phase === "current" || value.phase === "unavailable" || value.phase === "progress" || value.phase === "done" ? value.phase : "failed",
        title: typeof value.title === "string" ? value.title : "",
        body: typeof value.body === "string" ? value.body : "",
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
    marks: parseMarks(value.marks),
    rows: parseMarks(value.rows),
    spans: parseSpans(value.spans),
  }
}

function parseMarks(value: unknown): OtoMark[] {
  if (!Array.isArray(value))
    return []
  return value.flatMap((item) => {
    if (!isRecord(item) || typeof item.alias !== "string")
      return []
    const tuned = Array.isArray(item.tuned) ? item.tuned.map((flag) => flag === true) : [false, false, false, false, false]
    return [{
      alias: item.alias,
      offset: numberOr(item.offset, 0),
      overlap: numberOr(item.overlap, 0),
      preutter: numberOr(item.preutter, 0),
      consonant: numberOr(item.consonant, 0),
      blank: numberOr(item.blank, 0),
      file: typeof item.file === "string" ? item.file : "",
      index: numberOr(item.index, 0),
      autoAlias: typeof item.autoAlias === "string" ? item.autoAlias : item.alias,
      name: typeof item.name === "string" ? item.name : "",
      comment: typeof item.comment === "string" ? item.comment : "",
      tuned,
      mode: typeof item.mode === "string" ? item.mode : "",
    }]
  })
}

function parseBank(value: Record<string, unknown>): BankEvent | null {
  if (typeof value.name !== "string" || typeof value.encoding !== "string")
    return null
  const profile = Array.isArray(value.profile)
    ? value.profile.flatMap((item) => {
        if (!isRecord(item))
          return []
        return [{
          label: typeof item.label === "string" ? item.label : "",
          value: typeof item.value === "string" ? item.value : "",
          fullwidth: item.fullwidth !== false,
        }]
      })
    : [{ label: "", value: "", fullwidth: true }]
  const locales = Array.isArray(value.locales)
    ? value.locales.flatMap((item) => {
        if (!isRecord(item))
          return []
        return [{ code: typeof item.code === "string" ? item.code : "", name: typeof item.name === "string" ? item.name : "" }]
      })
    : [{ code: "", name: "" }]
  return {
    type: "bank",
    generation: numberOr(value.generation, 0),
    name: value.name,
    image: typeof value.image === "string" ? value.image : "",
    sample: typeof value.sample === "string" ? value.sample : "",
    samples: strings(value.samples),
    author: typeof value.author === "string" ? value.author : "",
    web: typeof value.web === "string" ? value.web : "",
    profile: profile.length > 0 ? profile : [{ label: "", value: "", fullwidth: true }],
    extra: typeof value.extra === "string" ? value.extra : "",
    readme: typeof value.readme === "string" ? value.readme : "",
    encoding: value.encoding,
    voice: typeof value.voice === "string" ? value.voice : "",
    version: typeof value.version === "string" ? value.version : "",
    locales: locales.length > 0 ? locales : [{ code: "", name: "" }],
    languages: Array.isArray(value.languages)
      ? value.languages.flatMap((item) => {
          if (!isRecord(item) || typeof item.code !== "string" || typeof item.label !== "string")
            return []
          return [{ code: item.code, label: item.label }]
        })
      : [],
    portrait: typeof value.portrait === "string" ? value.portrait : "",
    opacity: numberOr(value.opacity, 0.67),
    height: numberOr(value.height, 0),
    icon: typeof value.icon === "string" ? value.icon : null,
    portraitImage: typeof value.portraitImage === "string" ? value.portraitImage : null,
  }
}

function parseEffects(value: Record<string, unknown>): EffectsEvent | null {
  if (!Array.isArray(value.gains) || !Array.isArray(value.presets))
    return null
  return {
    type: "effects",
    preset: typeof value.preset === "string" ? value.preset : "カスタム",
    presets: strings(value.presets),
    noiseEnabled: value.noiseEnabled === true,
    noiseReduction: numberOr(value.noiseReduction, 8),
    noiseFiles: strings(value.noiseFiles),
    noiseFile: typeof value.noiseFile === "string" ? value.noiseFile : "",
    noiseStart: numberOr(value.noiseStart, 0),
    noiseEnd: numberOr(value.noiseEnd, 0),
    noiseReady: value.noiseReady === true,
    highpassEnabled: value.highpassEnabled === true,
    highpassHz: numberOr(value.highpassHz, 80),
    highpassSlope: numberOr(value.highpassSlope, 12),
    eqEnabled: value.eqEnabled === true,
    gains: value.gains.filter((item): item is number => typeof item === "number").slice(0, 8),
    compressorEnabled: value.compressorEnabled === true,
    compressorRatio: numberOr(value.compressorRatio, 2.5),
    compressorReduction: numberOr(value.compressorReduction, 3),
    normalizeEnabled: value.normalizeEnabled === true,
    normalizeTarget: numberOr(value.normalizeTarget, -1),
    noiseNote: typeof value.noiseNote === "string" ? value.noiseNote : null,
    order: strings(value.order),
  }
}

function parseRecList(value: Record<string, unknown>): RecListEvent | null {
  if (typeof value.path !== "string" || !Array.isArray(value.lines) || !Array.isArray(value.outside))
    return null
  return {
    type: "recList",
    path: value.path,
    recent: Array.isArray(value.recent)
      ? value.recent.flatMap((item) => {
          if (!isRecord(item) || typeof item.path !== "string" || typeof item.label !== "string")
            return []
          return [{ path: item.path, label: item.label }]
        })
      : [],
    lines: value.lines.flatMap((item) => {
      if (!isRecord(item) || typeof item.sing !== "string" || typeof item.wav !== "string")
        return []
      return [{
        sing: item.sing,
        comment: typeof item.comment === "string" ? item.comment : "",
        error: typeof item.error === "string" ? item.error : null,
        recorded: item.recorded === true,
        wav: item.wav,
      }]
    }),
    outside: value.outside.filter((item): item is string => typeof item === "string"),
  }
}

function parseSpans(value: unknown): PhoneSpan[] {
  if (!Array.isArray(value))
    return []
  return value.flatMap((item) => {
    if (!isRecord(item) || typeof item.label !== "string")
      return []
    return [{
      start: numberOr(item.start, 0),
      end: numberOr(item.end, 0),
      label: item.label,
      token: typeof item.token === "string" ? item.token : "",
    }]
  })
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
