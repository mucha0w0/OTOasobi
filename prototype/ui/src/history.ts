import { sameName } from "./filePick"
import type { BankEvent, BankLocale, BankRow, EffectsEvent, OtoMark, OtoState, PageCommand } from "./protocol"
import type { PhoneSpan } from "./spans"
import type { ThemeChoice } from "./theme"

export const historyLimit = 100
export const coalesceMs = 400

export type FrqRadios = {
  mode: "all" | "missing" | "selected"
  preutter: "keep" | "zero" | "none"
}

export type RecSnapshot = {
  note: string
  semitones: number
  speed: number
  volume: number
  countBeats: number
  countBeatsAfter: number
  bpm: number
  moras: number
  moraBeats: number
  advance: boolean
  recStartAfter: number
  recEndBefore: number
}

export type BankSnapshot = {
  generation: number
  name: string
  image: string
  sample: string
  author: string
  web: string
  profile: BankRow[]
  extra: string
  readme: string
  voice: string
  version: string
  locales: BankLocale[]
  portrait: string
  opacity: number
  height: number
}

export type AppearanceSnapshot = {
  theme: ThemeChoice
  accent: string | null
  scale: number
  lockPanels: boolean
}

export type EditSnapshot = {
  oto: OtoState | null
  marks: OtoMark[]
  spans: PhoneSpan[]
  effects: EffectsEvent | null
  bank: BankSnapshot | null
  appearance: AppearanceSnapshot
  devices: { inputId: string; outputId: string }
  monitor: boolean
  rec: RecSnapshot
  guide: { manual: boolean; path: string }
  recListPath: string
}

export type PendingEdit = {
  key: string
  coalesce: boolean
  undo: PageCommand[]
  redo: PageCommand[]
  undoFrq?: FrqRadios
  redoFrq?: FrqRadios
}

export type HistoryStep = PendingEdit & { at: number }

export type History = {
  undo: HistoryStep[]
  redo: HistoryStep[]
}

const pointKeys = ["offset", "overlap", "preutter", "consonant", "blank"] as const

export function emptyHistory(): History {
  return { undo: [], redo: [] }
}

export function emptySnapshot(): EditSnapshot {
  return {
    oto: null,
    marks: [],
    spans: [],
    effects: null,
    bank: null,
    appearance: { theme: "system", accent: null, scale: 1, lockPanels: false },
    devices: { inputId: "", outputId: "" },
    monitor: false,
    rec: {
      note: "C4",
      semitones: 0,
      speed: 1,
      volume: 0.7,
      countBeats: 4,
      countBeatsAfter: 0,
      bpm: 120,
      moras: 5,
      moraBeats: 1,
      advance: false,
      recStartAfter: 0,
      recEndBefore: 0,
    },
    guide: { manual: true, path: "" },
    recListPath: "",
  }
}

export function pushStep(history: History, step: HistoryStep): History {
  const top = history.undo[history.undo.length - 1]
  if (top && step.coalesce && top.coalesce && top.key === step.key && step.at - top.at <= coalesceMs) {
    const undo = history.undo.slice(0, -1)
    undo.push({ ...top, at: step.at, redo: step.redo, redoFrq: step.redoFrq ?? top.redoFrq })
    return { undo, redo: [] }
  }
  const undo = [...history.undo, step]
  if (undo.length > historyLimit)
    undo.splice(0, undo.length - historyLimit)
  return { undo, redo: [] }
}

export function groupEdits(edits: PendingEdit[], at: number): HistoryStep | null {
  if (edits.length === 0)
    return null
  if (edits.length === 1) {
    const edit = edits[0]!
    return { ...edit, at }
  }
  return {
    key: `group:${edits.map((edit) => edit.key).join("|")}`,
    coalesce: false,
    at,
    undo: edits.reduceRight((commands, edit) => commands.concat(edit.undo), [] as PageCommand[]),
    redo: edits.flatMap((edit) => edit.redo),
  }
}

export function undoHistory(history: History): { history: History; step: HistoryStep } | null {
  const step = history.undo[history.undo.length - 1]
  if (!step)
    return null
  return {
    history: { undo: history.undo.slice(0, -1), redo: [...history.redo, step] },
    step,
  }
}

export function redoHistory(history: History): { history: History; step: HistoryStep } | null {
  const step = history.redo[history.redo.length - 1]
  if (!step)
    return null
  return {
    history: { undo: [...history.undo, step], redo: history.redo.slice(0, -1) },
    step,
  }
}

export function clearsHistory(command: PageCommand): boolean {
  if (command.type === "loadSettings")
    return true
  if (command.type === "effects" && command.op === "apply")
    return true
  return command.type === "oto" && (command.op === "analyze" || command.op === "analyzeFile")
}

export function textFieldOwnsUndo(text: boolean, value: string, committed: string | null): boolean {
  return text && committed != null && value !== committed
}

export function frqStep(current: FrqRadios, next: FrqRadios, at: number): HistoryStep | null {
  if (current.mode === next.mode && current.preutter === next.preutter)
    return null
  const key = current.mode !== next.mode && current.preutter !== next.preutter
    ? "frq.radios"
    : current.mode !== next.mode ? "frq.mode" : "frq.preutter"
  return { key, coalesce: false, at, undo: [], redo: [], undoFrq: current, redoFrq: next }
}

export function bankFromEvent(event: BankEvent): BankSnapshot {
  return {
    generation: event.generation,
    name: event.name,
    image: event.image,
    sample: event.sample,
    author: event.author,
    web: event.web,
    profile: event.profile.map((row) => ({ ...row })),
    extra: event.extra,
    readme: event.readme,
    voice: event.voice,
    version: event.version,
    locales: event.locales.map((row) => ({ ...row })),
    portrait: event.portrait,
    opacity: event.opacity,
    height: event.height,
  }
}

export function absorbOto(snapshot: EditSnapshot, event: OtoState): EditSnapshot {
  const rows = event.rows ?? snapshot.oto?.rows ?? []
  const oto = { ...event, rows: rows.map((mark) => ({ ...mark })) }
  return {
    ...snapshot,
    oto,
    marks: oto.marks.map((mark) => ({ ...mark })),
    spans: oto.spans.map((span) => ({ ...span })),
  }
}

export function absorbEffects(snapshot: EditSnapshot, effects: EffectsEvent): EditSnapshot {
  return { ...snapshot, effects: { ...effects, gains: [...effects.gains], presets: [...effects.presets], noiseFiles: [...effects.noiseFiles], order: [...effects.order] } }
}

export function absorbBank(snapshot: EditSnapshot, event: BankEvent): EditSnapshot {
  return { ...snapshot, bank: bankFromEvent(event) }
}

export function describeEdit(command: PageCommand, snapshot: EditSnapshot): PendingEdit | null {
  switch (command.type) {
    case "oto":
      return describeOto(command, snapshot)
    case "effects":
      return describeEffects(command, snapshot)
    case "bank":
      return describeBank(command, snapshot)
    case "appearance":
      return describeAppearance(command, snapshot)
    case "setDevices":
      return describeDevices(command, snapshot)
    case "monitor":
      return command.on === snapshot.monitor ? null : edit("monitor", false, [{ type: "monitor", on: snapshot.monitor }], [command])
    case "recSettings":
      return describeRec(command, snapshot)
    case "guideFile":
      return describeGuide(command, snapshot)
    case "recList":
      return describeRecList(command, snapshot)
    default:
      return null
  }
}

export function applySnapshot(snapshot: EditSnapshot, command: PageCommand): EditSnapshot {
  switch (command.type) {
    case "oto":
      return applyOto(snapshot, command)
    case "effects":
      return applyEffects(snapshot, command)
    case "bank":
      return {
        ...snapshot,
        bank: {
          generation: command.generation,
          name: command.name,
          image: command.image,
          sample: command.sample,
          author: command.author,
          web: command.web,
          profile: command.profile.map((row) => ({ ...row })),
          extra: command.extra,
          readme: command.readme,
          voice: command.voice,
          version: command.version,
          locales: command.locales.map((row) => ({ ...row })),
          portrait: command.portrait,
          opacity: command.opacity,
          height: command.height,
        },
      }
    case "appearance":
      return {
        ...snapshot,
        appearance: { theme: command.theme, accent: command.accent, scale: command.scale, lockPanels: command.lockPanels },
      }
    case "setDevices":
      return { ...snapshot, devices: { inputId: command.inputId, outputId: command.outputId } }
    case "monitor":
      return { ...snapshot, monitor: command.on }
    case "recSettings":
      return {
        ...snapshot,
        rec: {
          note: command.note,
          semitones: command.semitones,
          speed: command.speed,
          volume: command.volume,
          countBeats: command.countBeats,
          countBeatsAfter: command.countBeatsAfter,
          bpm: command.bpm,
          moras: command.moras,
          moraBeats: command.moraBeats,
          advance: command.advance,
          recStartAfter: command.recStartAfter,
          recEndBefore: command.recEndBefore,
        },
      }
    case "guideFile":
      return {
        ...snapshot,
        guide: {
          manual: command.manual ?? snapshot.guide.manual,
          path: command.path ?? snapshot.guide.path,
        },
      }
    case "recList":
      if (command.op === "none")
        return { ...snapshot, recListPath: "" }
      if (command.op === "open")
        return { ...snapshot, recListPath: command.path ?? "" }
      return snapshot
    default:
      return snapshot
  }
}

export function applyCommands(snapshot: EditSnapshot, commands: PageCommand[]): EditSnapshot {
  return commands.reduce(applySnapshot, snapshot)
}

function edit(key: string, coalesce: boolean, undo: PageCommand[], redo: PageCommand[]): PendingEdit {
  return { key, coalesce, undo, redo }
}

function near(left: number, right: number) {
  return Math.abs(left - right) < 1e-6
}

function slider(group: string, key: string, value: number): PageCommand {
  return { type: "oto", op: "slider", group, key, value }
}

function readSlider(oto: OtoState, group: string, key: string): number | null {
  switch (group) {
    case "mono":
      if (key === "offset")
        return oto.offset
      if (key === "consonant")
        return oto.consonant
      if (key === "blank")
        return oto.blank
      return null
    case "monoOverlap":
      return key in oto.overlap ? oto.overlap[key]! : null
    case "renzoku":
      if (key === "offset")
        return oto.renzokuOffset
      if (key === "consonant")
        return oto.renzokuConsonant
      if (key === "blank")
        return oto.renzokuBlank
      if (key === "overlap")
        return oto.renzokuOverlap
      return null
    case "cvvc":
      if (key === "consonant")
        return oto.cvvcConsonant
      if (key === "blank")
        return oto.cvvcBlank
      if (key === "vcOffset")
        return oto.vcOffset
      if (key === "vcOverlap")
        return oto.vcOverlap
      return null
    case "cvvcOverlap":
      return key in oto.cvvcOverlap ? oto.cvvcOverlap[key]! : null
    case "vcConsonant":
      return key in oto.vcConsonant ? oto.vcConsonant[key]! : null
    case "vcBlank":
      return key in oto.vcBlank ? oto.vcBlank[key]! : null
    default:
      return null
  }
}

function writeSlider(oto: OtoState, group: string, key: string, value: number): OtoState | null {
  switch (group) {
    case "mono":
      if (key === "offset")
        return { ...oto, offset: value }
      if (key === "consonant")
        return { ...oto, consonant: value }
      if (key === "blank")
        return { ...oto, blank: value }
      return null
    case "monoOverlap":
      return { ...oto, overlap: { ...oto.overlap, [key]: value } }
    case "renzoku":
      if (key === "offset")
        return { ...oto, renzokuOffset: value }
      if (key === "consonant")
        return { ...oto, renzokuConsonant: value }
      if (key === "blank")
        return { ...oto, renzokuBlank: value }
      if (key === "overlap")
        return { ...oto, renzokuOverlap: value }
      return null
    case "cvvc":
      if (key === "consonant")
        return { ...oto, cvvcConsonant: value }
      if (key === "blank")
        return { ...oto, cvvcBlank: value }
      if (key === "vcOffset")
        return { ...oto, vcOffset: value }
      if (key === "vcOverlap")
        return { ...oto, vcOverlap: value }
      return null
    case "cvvcOverlap":
      return { ...oto, cvvcOverlap: { ...oto.cvvcOverlap, [key]: value } }
    case "vcConsonant":
      return { ...oto, vcConsonant: { ...oto.vcConsonant, [key]: value } }
    case "vcBlank":
      return { ...oto, vcBlank: { ...oto.vcBlank, [key]: value } }
    default:
      return null
  }
}

function monoSliders(oto: OtoState): PageCommand[] {
  return [
    slider("mono", "offset", oto.offset),
    slider("mono", "consonant", oto.consonant),
    slider("mono", "blank", oto.blank),
    ...Object.entries(oto.overlap).map(([key, value]) => slider("monoOverlap", key, value)),
  ]
}

function cvvcSliders(oto: OtoState): PageCommand[] {
  return [
    slider("cvvc", "consonant", oto.cvvcConsonant),
    slider("cvvc", "blank", oto.cvvcBlank),
    slider("cvvc", "vcOffset", oto.vcOffset),
    slider("cvvc", "vcOverlap", oto.vcOverlap),
    ...Object.entries(oto.cvvcOverlap).map(([key, value]) => slider("cvvcOverlap", key, value)),
    ...Object.entries(oto.vcConsonant).map(([key, value]) => slider("vcConsonant", key, value)),
    ...Object.entries(oto.vcBlank).map(([key, value]) => slider("vcBlank", key, value)),
  ]
}

function presetName(oto: OtoState, target: string) {
  if (target === "rest")
    return oto.restPreset
  if (target === "lead")
    return oto.leadPreset
  if (target === "cvvc")
    return oto.cvvcPreset
  return oto.standalonePreset
}

function describeOto(command: Extract<PageCommand, { type: "oto" }>, snapshot: EditSnapshot): PendingEdit | null {
  const oto = snapshot.oto
  if (!oto || command.op === "analyze" || command.op === "analyzeFile" || command.op === "write" || command.op === "savePreset")
    return null
  switch (command.op) {
    case "mode":
      return command.mode === oto.mode ? null : edit("oto.mode", false, [{ type: "oto", op: "mode", mode: oto.mode }], [command])
    case "slider": {
      const current = readSlider(oto, command.group, command.key)
      if (current == null || near(current, command.value))
        return null
      return edit(`oto.slider.${command.group}.${command.key}`, true, [slider(command.group, command.key, current)], [command])
    }
    case "preset": {
      if (command.target === "rest" || command.target === "lead") {
        if (oto.restPreset === command.name && oto.leadPreset === command.name)
          return null
        return edit(`oto.preset.${command.target}`, false, [
          { type: "oto", op: "preset", target: "rest", name: oto.restPreset },
          { type: "oto", op: "preset", target: "lead", name: oto.leadPreset },
        ], [command])
      }
      const current = presetName(oto, command.target)
      if (current === command.name)
        return null
      const restore: PageCommand[] = [{ type: "oto", op: "preset", target: command.target, name: current }]
      if (command.target === "cvvc")
        restore.push(...cvvcSliders(oto))
      else
        restore.push(...monoSliders(oto))
      return edit(`oto.preset.${command.target}`, false, restore, [command])
    }
    case "alias": {
      const current = command.which === "renzoku" ? oto.aliasRenzoku : command.which === "cvvc" ? oto.aliasCvvc : oto.aliasMono
      if (current === command.on)
        return null
      return edit(`oto.alias.${command.which}`, false, [{ type: "oto", op: "alias", which: command.which, on: current }], [command])
    }
    case "scope":
      return command.scope === oto.scope ? null : edit("oto.scope", false, [{ type: "oto", op: "scope", scope: oto.scope }], [command])
    case "tune": {
      const key = pointKeys[command.point]
      const mark = snapshot.marks.find((item) => item.index === command.index && (item.file.length === 0 || sameName(item.file, command.file)))
      if (!key || !mark || near(mark[key], command.time))
        return null
      return edit(
        `oto.tune.${command.file}.${command.index}.${command.point}`,
        false,
        [{ type: "oto", op: "tune", file: command.file, index: command.index, point: command.point, time: mark[key] }],
        [command],
      )
    }
    case "spans": {
      if (snapshot.spans.length !== command.spans.length)
        return null
      const same = snapshot.spans.every((span, index) => near(span.start, command.spans[index]!.start) && near(span.end, command.spans[index]!.end))
      if (same)
        return null
      return edit(
        `oto.spans.${command.file}`,
        false,
        [{ type: "oto", op: "spans", file: command.file, spans: snapshot.spans.map((span) => ({ start: span.start, end: span.end })) }],
        [command],
      )
    }
    case "fields": {
      const mark = findPlaced(snapshot, command.file, command.index, command.alias, command.mode)
      if (!mark || sameFields(mark, command))
        return null
      return edit(`oto.fields.${command.file}.${command.index}.${command.alias}`, false, [{
        type: "oto",
        op: "fields",
        file: command.file,
        index: command.index,
        alias: command.alias,
        mode: command.mode,
        offset: mark.offset,
        overlap: mark.overlap,
        preutter: mark.preutter,
        consonant: mark.consonant,
        blank: mark.blank,
      }], [command])
    }
    case "aliasName": {
      const mark = findPlaced(snapshot, command.file, command.index, command.alias, command.mode)
      if (!mark || mark.name === command.name)
        return null
      return edit(`oto.aliasName.${command.file}.${command.index}`, false, [{
        type: "oto",
        op: "aliasName",
        file: command.file,
        index: command.index,
        alias: command.alias,
        mode: command.mode,
        name: mark.name,
      }], [command])
    }
    case "comment": {
      const mark = findPlaced(snapshot, command.file, command.index, command.alias, command.mode)
      if (!mark || mark.comment === command.comment)
        return null
      return edit(`oto.comment.${command.file}.${command.index}`, false, [{
        type: "oto",
        op: "comment",
        file: command.file,
        index: command.index,
        alias: command.alias,
        mode: command.mode,
        comment: mark.comment,
      }], [command])
    }
    default:
      return null
  }
}

function sameFields(mark: OtoMark, command: Extract<PageCommand, { type: "oto"; op: "fields" }>) {
  return near(mark.offset, command.offset)
    && near(mark.overlap, command.overlap)
    && near(mark.preutter, command.preutter)
    && near(mark.consonant, command.consonant)
    && near(mark.blank, command.blank)
}

function findPlaced(snapshot: EditSnapshot, file: string, index: number, alias: string, mode?: string) {
  const rows = snapshot.oto?.rows ?? []
  const pool = rows.length > 0 ? rows : snapshot.marks
  return pool.find((mark) => mark.index === index
    && sameName(mark.alias, alias)
    && (mark.file.length === 0 || sameName(mark.file, file))
    && (mode == null || mark.mode == null || mark.mode === mode))
}

function withOto(snapshot: EditSnapshot, oto: OtoState, marks = oto.marks, spans = snapshot.spans): EditSnapshot {
  return { ...snapshot, oto, marks, spans }
}

function applyOto(snapshot: EditSnapshot, command: Extract<PageCommand, { type: "oto" }>): EditSnapshot {
  const oto = snapshot.oto
  if (!oto)
    return snapshot
  switch (command.op) {
    case "mode":
      return withOto(snapshot, { ...oto, mode: command.mode === "renzoku" || command.mode === "cvvc" ? command.mode : "mono" })
    case "slider": {
      const next = writeSlider(oto, command.group, command.key, command.value)
      return next ? withOto(snapshot, next) : snapshot
    }
    case "preset": {
      if (command.target === "rest" || command.target === "lead")
        return withOto(snapshot, { ...oto, restPreset: command.name, leadPreset: command.name })
      if (command.target === "cvvc")
        return withOto(snapshot, { ...oto, cvvcPreset: command.name })
      return withOto(snapshot, { ...oto, standalonePreset: command.name })
    }
    case "alias":
      if (command.which === "renzoku")
        return withOto(snapshot, { ...oto, aliasRenzoku: command.on })
      if (command.which === "cvvc")
        return withOto(snapshot, { ...oto, aliasCvvc: command.on })
      return withOto(snapshot, { ...oto, aliasMono: command.on })
    case "scope":
      return withOto(snapshot, { ...oto, scope: command.scope === "missing" || command.scope === "overlap" ? command.scope : "all" })
    case "tune": {
      const key = pointKeys[command.point]
      if (!key)
        return snapshot
      const marks = snapshot.marks.map((mark) => mark.index === command.index ? { ...mark, [key]: command.time } : mark)
      return withOto(snapshot, { ...oto, marks }, marks)
    }
    case "spans": {
      const spans = snapshot.spans.map((span, index) => {
        const next = command.spans[index]
        return next ? { ...span, start: next.start, end: next.end } : span
      })
      return withOto(snapshot, { ...oto, spans }, snapshot.marks, spans)
    }
    case "fields":
      return withPlaced(snapshot, command.file, command.index, command.alias, command.mode, (mark) => ({
        ...mark,
        offset: command.offset,
        overlap: command.overlap,
        preutter: command.preutter,
        consonant: command.consonant,
        blank: command.blank,
      }))
    case "aliasName":
      return withPlaced(snapshot, command.file, command.index, command.alias, command.mode, (mark) => ({ ...mark, name: command.name }))
    case "comment":
      return withPlaced(snapshot, command.file, command.index, command.alias, command.mode, (mark) => ({ ...mark, comment: command.comment }))
    default:
      return snapshot
  }
}

function withPlaced(
  snapshot: EditSnapshot,
  file: string,
  index: number,
  alias: string,
  mode: string | undefined,
  map: (mark: OtoMark) => OtoMark,
): EditSnapshot {
  const hit = (mark: OtoMark) => mark.index === index
    && sameName(mark.alias, alias)
    && (mark.file.length === 0 || sameName(mark.file, file))
    && (mode == null || mark.mode == null || mark.mode === mode)
  const oto = snapshot.oto
  if (!oto)
    return snapshot
  const rows = oto.rows?.map((mark) => hit(mark) ? map(mark) : mark) ?? null
  const marks = snapshot.marks.map((mark) => hit(mark) ? map(mark) : mark)
  return withOto(snapshot, { ...oto, rows, marks }, marks)
}

function effectsSet(effects: EffectsEvent): PageCommand {
  return {
    type: "effects",
    op: "set",
    noiseEnabled: effects.noiseEnabled,
    noiseReduction: effects.noiseReduction,
    highpassEnabled: effects.highpassEnabled,
    highpassHz: effects.highpassHz,
    highpassSlope: effects.highpassSlope,
    eqEnabled: effects.eqEnabled,
    gains: [...effects.gains],
    compressorEnabled: effects.compressorEnabled,
    compressorRatio: effects.compressorRatio,
    compressorReduction: effects.compressorReduction,
    normalizeEnabled: effects.normalizeEnabled,
    normalizeTarget: effects.normalizeTarget,
  }
}

function describeEffects(command: Extract<PageCommand, { type: "effects" }>, snapshot: EditSnapshot): PendingEdit | null {
  const effects = snapshot.effects
  if (!effects || command.op === "apply")
    return null
  switch (command.op) {
    case "set": {
      const changed = effectChanges(effects, command)
      if (changed.length === 0)
        return null
      const key = changed.length === 1 ? `effects.${changed[0]}` : `effects.${changed.join("+")}`
      return edit(key, changed.length === 1, [effectsSet(effects)], [command])
    }
    case "preset":
      return command.name === effects.preset ? null : edit("effects.preset", false, [effectsSet(effects)], [command])
    case "noise":
      if (sameNoise(effects, command))
        return null
      return edit(noiseKey(effects, command), noiseCoalesce(effects, command), [noiseUndo(effects)], [command])
    default:
      return null
  }
}

function effectChanges(effects: EffectsEvent, command: Extract<PageCommand, { type: "effects"; op: "set" }>) {
  const changed: string[] = []
  if (effects.noiseEnabled !== command.noiseEnabled)
    changed.push("noiseEnabled")
  if (!near(effects.noiseReduction, command.noiseReduction))
    changed.push("noiseReduction")
  if (effects.highpassEnabled !== command.highpassEnabled)
    changed.push("highpassEnabled")
  if (!near(effects.highpassHz, command.highpassHz))
    changed.push("highpassHz")
  if (!near(effects.highpassSlope, command.highpassSlope))
    changed.push("highpassSlope")
  if (effects.eqEnabled !== command.eqEnabled)
    changed.push("eqEnabled")
  command.gains.forEach((gain, index) => {
    if (!near(effects.gains[index] ?? 0, gain))
      changed.push(`gains.${index}`)
  })
  if (effects.compressorEnabled !== command.compressorEnabled)
    changed.push("compressorEnabled")
  if (!near(effects.compressorRatio, command.compressorRatio))
    changed.push("compressorRatio")
  if (!near(effects.compressorReduction, command.compressorReduction))
    changed.push("compressorReduction")
  if (effects.normalizeEnabled !== command.normalizeEnabled)
    changed.push("normalizeEnabled")
  if (!near(effects.normalizeTarget, command.normalizeTarget))
    changed.push("normalizeTarget")
  return changed
}

function noiseHasRange(command: Extract<PageCommand, { type: "effects"; op: "noise" }>) {
  return command.start != null && command.end != null
}

function sameNoise(effects: EffectsEvent, command: Extract<PageCommand, { type: "effects"; op: "noise" }>) {
  if (command.file !== effects.noiseFile)
    return false
  if (!noiseHasRange(command))
    return !effects.noiseReady
  return effects.noiseReady && near(command.start ?? 0, effects.noiseStart) && near(command.end ?? 0, effects.noiseEnd)
}

function noiseUndo(effects: EffectsEvent): Extract<PageCommand, { type: "effects"; op: "noise" }> {
  if (!effects.noiseReady)
    return { type: "effects", op: "noise", file: effects.noiseFile }
  return { type: "effects", op: "noise", file: effects.noiseFile, start: effects.noiseStart, end: effects.noiseEnd }
}

function noiseKey(effects: EffectsEvent, command: Extract<PageCommand, { type: "effects"; op: "noise" }>) {
  const changed = [
    command.file !== effects.noiseFile ? "file" : "",
    noiseHasRange(command) && !near(command.start ?? 0, effects.noiseStart) ? "start" : "",
    noiseHasRange(command) && !near(command.end ?? 0, effects.noiseEnd) ? "end" : "",
    !noiseHasRange(command) && effects.noiseReady ? "range" : "",
  ].filter((item) => item.length > 0)
  return changed.length === 1 ? `effects.noise.${changed[0]}` : "effects.noise"
}

function noiseCoalesce(effects: EffectsEvent, command: Extract<PageCommand, { type: "effects"; op: "noise" }>) {
  if (command.file !== effects.noiseFile || !noiseHasRange(command))
    return false
  const start = !near(command.start ?? 0, effects.noiseStart)
  const end = !near(command.end ?? 0, effects.noiseEnd)
  return start !== end
}

function applyEffects(snapshot: EditSnapshot, command: Extract<PageCommand, { type: "effects" }>): EditSnapshot {
  const effects = snapshot.effects
  if (!effects)
    return snapshot
  switch (command.op) {
    case "set":
      return {
        ...snapshot,
        effects: {
          ...effects,
          preset: "カスタム",
          noiseEnabled: command.noiseEnabled,
          noiseReduction: command.noiseReduction,
          highpassEnabled: command.highpassEnabled,
          highpassHz: command.highpassHz,
          highpassSlope: command.highpassSlope,
          eqEnabled: command.eqEnabled,
          gains: [...command.gains],
          compressorEnabled: command.compressorEnabled,
          compressorRatio: command.compressorRatio,
          compressorReduction: command.compressorReduction,
          normalizeEnabled: command.normalizeEnabled,
          normalizeTarget: command.normalizeTarget,
        },
      }
    case "preset":
      return { ...snapshot, effects: { ...effects, preset: command.name } }
    case "noise":
      return {
        ...snapshot,
        effects: {
          ...effects,
          noiseFile: command.file,
          noiseStart: command.start ?? 0,
          noiseEnd: command.end ?? 0,
          noiseReady: command.start != null && command.end != null,
          noiseMessage: null,
        },
      }
    default:
      return snapshot
  }
}

function bankCommand(bank: BankSnapshot): PageCommand {
  return {
    type: "bank",
    generation: bank.generation,
    name: bank.name,
    image: bank.image,
    sample: bank.sample,
    author: bank.author,
    web: bank.web,
    profile: bank.profile.map((row) => ({ ...row })),
    extra: bank.extra,
    readme: bank.readme,
    voice: bank.voice,
    version: bank.version,
    locales: bank.locales.map((row) => ({ ...row })),
    portrait: bank.portrait,
    opacity: bank.opacity,
    height: bank.height,
  }
}

function describeBank(command: Extract<PageCommand, { type: "bank" }>, snapshot: EditSnapshot): PendingEdit | null {
  const bank = snapshot.bank
  if (!bank)
    return null
  const changed = [
    command.name !== bank.name ? "name" : "",
    command.image !== bank.image ? "image" : "",
    command.sample !== bank.sample ? "sample" : "",
    command.author !== bank.author ? "author" : "",
    command.web !== bank.web ? "web" : "",
    JSON.stringify(command.profile) !== JSON.stringify(bank.profile) ? "profile" : "",
    command.extra !== bank.extra ? "extra" : "",
    command.readme !== bank.readme ? "readme" : "",
    command.voice !== bank.voice ? "voice" : "",
    command.version !== bank.version ? "version" : "",
    JSON.stringify(command.locales) !== JSON.stringify(bank.locales) ? "locales" : "",
    command.portrait !== bank.portrait ? "portrait" : "",
    near(command.opacity, bank.opacity) ? "" : "opacity",
    command.height !== bank.height ? "height" : "",
  ].filter((item) => item.length > 0)
  if (changed.length === 0)
    return null
  const key = changed.length === 1 ? `bank.${changed[0]}` : `bank.${changed.join("+")}`
  return edit(key, changed.length === 1, [bankCommand(bank)], [command])
}

function describeAppearance(command: Extract<PageCommand, { type: "appearance" }>, snapshot: EditSnapshot): PendingEdit | null {
  const current = snapshot.appearance
  const changed = [
    command.theme !== current.theme ? "theme" : "",
    command.accent !== current.accent ? "accent" : "",
    near(command.scale, current.scale) ? "" : "scale",
    command.lockPanels !== current.lockPanels ? "lockPanels" : "",
  ].filter((item) => item.length > 0)
  if (changed.length === 0)
    return null
  const key = changed.length === 1 ? `appearance.${changed[0]}` : `appearance.${changed.join("+")}`
  return edit(key, changed.length === 1 && changed[0] === "scale", [{
    type: "appearance",
    theme: current.theme,
    accent: current.accent,
    scale: current.scale,
    lockPanels: current.lockPanels,
  }], [command])
}

function describeDevices(command: Extract<PageCommand, { type: "setDevices" }>, snapshot: EditSnapshot): PendingEdit | null {
  const changed = [
    command.inputId !== snapshot.devices.inputId ? "input" : "",
    command.outputId !== snapshot.devices.outputId ? "output" : "",
  ].filter((item) => item.length > 0)
  if (changed.length === 0)
    return null
  const key = changed.length === 1 ? `devices.${changed[0]}` : "devices"
  return edit(key, false, [{ type: "setDevices", inputId: snapshot.devices.inputId, outputId: snapshot.devices.outputId }], [command])
}

function describeRec(command: Extract<PageCommand, { type: "recSettings" }>, snapshot: EditSnapshot): PendingEdit | null {
  const current = snapshot.rec
  const changed = [
    command.note !== current.note ? "note" : "",
    command.semitones !== current.semitones ? "semitones" : "",
    !near(command.speed, current.speed) ? "speed" : "",
    !near(command.volume, current.volume) ? "volume" : "",
    command.countBeats !== current.countBeats ? "countBeats" : "",
    command.countBeatsAfter !== current.countBeatsAfter ? "countBeatsAfter" : "",
    command.bpm !== current.bpm ? "bpm" : "",
    command.moras !== current.moras ? "moras" : "",
    !near(command.moraBeats, current.moraBeats) ? "moraBeats" : "",
    command.advance !== current.advance ? "advance" : "",
    !near(command.recStartAfter, current.recStartAfter) ? "recStartAfter" : "",
    !near(command.recEndBefore, current.recEndBefore) ? "recEndBefore" : "",
  ].filter((item) => item.length > 0)
  if (changed.length === 0)
    return null
  const numeric = changed.length === 1 && (changed[0] === "semitones" || changed[0] === "speed" || changed[0] === "volume" || changed[0] === "bpm" || changed[0] === "moras" || changed[0] === "moraBeats" || changed[0] === "countBeats" || changed[0] === "countBeatsAfter" || changed[0] === "recStartAfter" || changed[0] === "recEndBefore")
  const key = changed.length === 1 ? `rec.${changed[0]}` : `rec.${changed.join("+")}`
  return edit(key, numeric, [{ type: "recSettings", ...current }], [command])
}

function describeGuide(command: Extract<PageCommand, { type: "guideFile" }>, snapshot: EditSnapshot): PendingEdit | null {
  if (command.pick)
    return null
  if (command.manual != null && command.manual !== snapshot.guide.manual)
    return edit("guide.manual", false, [{ type: "guideFile", manual: snapshot.guide.manual }], [command])
  if (command.path != null && command.path !== snapshot.guide.path)
    return edit("guide.path", false, [{ type: "guideFile", path: snapshot.guide.path }], [command])
  return null
}

function describeRecList(command: Extract<PageCommand, { type: "recList" }>, snapshot: EditSnapshot): PendingEdit | null {
  if (command.op === "pick")
    return null
  const next = command.op === "none" ? "" : command.path ?? ""
  if (next === snapshot.recListPath)
    return null
  const undo: PageCommand = snapshot.recListPath.length === 0
    ? { type: "recList", op: "none" }
    : { type: "recList", op: "open", path: snapshot.recListPath }
  return edit("recList", false, [undo], [command])
}
