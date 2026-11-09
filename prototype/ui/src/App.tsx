import { useCallback, useEffect, useRef, useState } from "react"
import { BankPanel } from "./BankPanel"
import { Dock } from "./Dock"
import { EffectsPanel } from "./EffectsPanel"
import { aliasId, aliasPanelMode, aliasStepIndex, currentAliasId, findAlias, markIndexForAlias, pendingAliasReady, selectedAliasIds } from "./aliasList"
import { AliasList, FileList, type FileListHandle } from "./FileList"
import { applyFileClick, emptyFilePick, pruneFilePick, sameName, type FilePick } from "./filePick"
import { nextFileIndex } from "./fileStep"
import { FrqEditor } from "./FrqEditor"
import { FrqPanel } from "./FrqPanel"
import { GraphBand, type BandHandle, type BandKind, type GraphModel, type LiveTune } from "./GraphBand"
import {
  absorbBank,
  absorbEffects,
  absorbOto,
  applySnapshot,
  clearsHistory,
  describeEdit,
  emptyHistory,
  emptySnapshot,
  frqStep,
  groupEdits,
  pushStep,
  redoHistory,
  textFieldOwnsUndo,
  undoHistory,
  type EditSnapshot,
  type FrqRadios,
  type History,
  type PendingEdit,
} from "./history"
import { connectHost } from "./host"
import { contains, createDefault, hidePane, idsOf, parseLayout, pluginPaneId, registerPlugins, showPane, type LayoutNode, type PaneBox } from "./layout"
import { createLiveBus, type LiveBus } from "./live"
import { MenuBar } from "./MenuBar"
import { OtoTable } from "./OtoTable"
import { AnalyzePanel, OtoIni, OtoSettings } from "./OtoPanels"
import { PluginFrame, type PluginContext } from "./PluginFrame"
import { previewColumnLimit, previewRowLimit, previewStillCovers, requestsPreviewAfter, uiScale, type BankEvent, type DeviceInfo, type EffectsEvent, type FrqViewEvent, type HostEvent, type NoiseWaveEvent, type OtoMark, type OtoState, type PageCommand, type PluginItem, type PluginReply, type PreviewEvent, type RecListEvent, type SavedEvent } from "./protocol"
import { RecListPanel, RecScriptPanel } from "./RecPanels"
import { noticeTone, ToneIcon, ToneRow, type NoticeTone } from "./notice"
import { createPointerBus } from "./ruler"
import { PaneSlots } from "./paneSlots"
import { SettingsDialog, settingsTabs } from "./settings"
import { GuidePanel, MicPanel, RecPanel, type GuideFileState } from "./SidePanels"
import type { PhoneSpan } from "./spans"
import { buttonColors, correctAccent, defaultAccent, resolveTheme, type ThemeChoice } from "./theme"
import type { Range } from "./viewport"

function applyPageScale(scale: number) {
  document.documentElement.style.zoom = String(scale)
}

function applyPanelsLocked(locked: boolean) {
  if (locked)
    document.documentElement.dataset.panelsLocked = "1"
  else
    delete document.documentElement.dataset.panelsLocked
}

const pictureKeep = 12

type KeptPicture = { picture: PreviewEvent; columns: number }

function rememberPicture(cache: Map<string, KeptPicture>, name: string, picture: PreviewEvent, columns: number) {
  cache.delete(name)
  cache.set(name, { picture, columns })
  while (cache.size > pictureKeep) {
    const oldest = cache.keys().next().value
    if (oldest === undefined)
      break
    cache.delete(oldest)
  }
}

function mergePreview(current: PreviewEvent | null, event: PreviewEvent, keepSpec: boolean): PreviewEvent {
  const loudness = event.loudness.length > 0
    ? event.loudness
    : keepSpec && current ? current.loudness : []
  const hasWave = event.lo.length > 0
  const hasSpec = event.specColumns > 0 && event.spec.length > 0
  if (hasSpec && !hasWave && current)
    return {
      ...current,
      duration: event.duration || current.duration,
      spec: event.spec,
      specColumns: event.specColumns,
      specRows: event.specRows,
      specT0: event.specT0,
      specT1: event.specT1,
      png: "",
      loudness,
      sampleRate: event.sampleRate || current.sampleRate,
    }
  if (hasWave && !hasSpec && keepSpec && current && current.spec.length > 0)
    return {
      ...current,
      lo: event.lo,
      hi: event.hi,
      waveT0: event.waveT0,
      waveT1: event.waveT1,
      duration: event.duration || current.duration,
      sampleRate: event.sampleRate || current.sampleRate,
      png: "",
      loudness,
    }
  if (hasWave && !hasSpec)
    return { ...event, spec: "", specColumns: 0, specRows: 0, png: "", loudness }
  return { ...event, loudness }
}

const graphKinds: Record<string, BandKind> = {
  wave: "wave",
  spec: "spec",
  loud: "loud",
  otoBand: "oto",
}

function isTextControl(target: EventTarget | null): target is HTMLInputElement | HTMLTextAreaElement {
  if (target instanceof HTMLTextAreaElement)
    return true
  if (!(target instanceof HTMLInputElement))
    return false
  return !["button", "checkbox", "color", "file", "radio", "range", "reset", "submit"].includes(target.type)
}

function stampActiveText() {
  const target = document.activeElement
  if (!isTextControl(target))
    return
  target.dataset.history = target.value
}

export function App() {
  const sendRef = useRef<(command: PageCommand) => void>(() => undefined)
  const rawSend = useRef<(command: PageCommand) => void>(() => undefined)
  const applyRef = useRef<(event: HostEvent) => void>(() => undefined)
  const bands = useRef(new Set<BandHandle>())
  const boxes = useRef<Record<string, PaneBox>>({})
  const pictureRef = useRef<PreviewEvent | null>(null)
  const pictureName = useRef("")
  const loadingFile = useRef<string | null>(null)
  const wheeling = useRef(false)
  const wheelHold = useRef<{ picture: PreviewEvent | null; note: string; range: Range; pictureName: string } | null>(null)
  const pictureCache = useRef(new Map<string, KeptPicture>())
  const previewAsk = useRef({ t0: 0, t1: 0, columns: 0 })
  const otoRef = useRef<OtoState | null>(null)
  const otoGenRef = useRef(-1)
  const folderRef = useRef("")
  const rangeRef = useRef<Range>({ t0: 0, t1: 0 })
  const marksRef = useRef<OtoMark[]>([])
  const spansRef = useRef<PhoneSpan[]>([])
  const noteRef = useRef("")
  const missingRef = useRef(false)
  const focusRef = useRef(-1)
  const pendingFocus = useRef<{ file: string; index: number; alias?: string; autoAlias: string; mode?: string } | null>(null)
  const liveRef = useRef<LiveTune | null>(null)
  const draftRef = useRef<PhoneSpan[] | null>(null)
  const modelRef = useRef<GraphModel>({ picture: null, range: rangeRef.current, marks: [], spans: [], note: "", focus: -1, live: null, draft: null })
  const fileNameRef = useRef("")
  const namesRef = useRef<string[]>([])
  const previewTimer = useRef<number | undefined>(undefined)
  const fileStepTimer = useRef<number | undefined>(undefined)
  const stepTarget = useRef<number | null>(null)
  const aliasStepTarget = useRef<number | null>(null)
  const busRef = useRef<LiveBus | null>(null)
  if (!busRef.current)
    busRef.current = createLiveBus()
  const bus = busRef.current

  const [connected, setConnected] = useState(false)
  const [theme, setTheme] = useState<ThemeChoice>("system")
  const [scale, setScale] = useState(() => uiScale(Number.parseFloat(document.documentElement.style.zoom)))
  const [lockPanels, setLockPanels] = useState(() => document.documentElement.dataset.panelsLocked === "1")
  const [systemDark, setSystemDark] = useState(() => window.matchMedia("(prefers-color-scheme: dark)").matches)
  const [accent, setAccent] = useState<string | null>(null)
  const pointerState = useRef<ReturnType<typeof createPointerBus> | null>(null)
  if (!pointerState.current)
    pointerState.current = createPointerBus()
  const pointerBus = pointerState.current
  const [inputs, setInputs] = useState<DeviceInfo[]>([])
  const [outputs, setOutputs] = useState<DeviceInfo[]>([])
  const [inputId, setInputId] = useState("")
  const [outputId, setOutputId] = useState("")
  const [monitor, setMonitor] = useState(false)
  const [note, setNote] = useState("C4")
  const [semitones, setSemitones] = useState(0)
  const [speed, setSpeed] = useState(1)
  const [volume, setVolume] = useState(0.7)
  const [countIn, setCountIn] = useState(false)
  const [countBeats, setCountBeats] = useState(4)
  const [countBeatsAfter, setCountBeatsAfter] = useState(0)
  const [bpm, setBpm] = useState(120)
  const [moras, setMoras] = useState(5)
  const [recStartAfter, setRecStartAfter] = useState(0)
  const [recEndBefore, setRecEndBefore] = useState(0)
  const [advance, setAdvance] = useState(false)
  const [guidePlaying, setGuidePlaying] = useState(false)
  const [folder, setFolder] = useState("")
  const [names, setNames] = useState<string[]>([])
  const [fileName, setFileName] = useState("")
  const pickRef = useRef<FilePick>(emptyFilePick)
  const [pick, setPick] = useState<FilePick>(emptyFilePick)
  const fileListRef = useRef<FileListHandle>(null)
  const aliasListRef = useRef<FileListHandle>(null)
  const [focusIndex, setFocusIndex] = useState(-1)
  const [picture, setPicture] = useState<PreviewEvent | null>(null)
  const [notice, setNotice] = useState<{ message: string; tone: NoticeTone } | null>(null)
  const [saved, setSaved] = useState<SavedEvent | null>(null)
  const [settings, setSettings] = useState(false)
  const [layout, setLayout] = useState<LayoutNode | null>(() => createDefault())
  const [oto, setOto] = useState<OtoState | null>(null)
  const [saveOpen, setSaveOpen] = useState(false)
  const [saveName, setSaveName] = useState("")
  const [loadOpen, setLoadOpen] = useState(false)
  const [replaceName, setReplaceName] = useState<string | null>(null)
  const [reanalyzeOpen, setReanalyzeOpen] = useState(false)
  const [oneOpen, setOneOpen] = useState(false)
  const [status, setStatus] = useState("")
  const [waiting, setWaiting] = useState(false)
  const [frqCount, setFrqCount] = useState({ wavs: 0, frqs: 0, busy: false })
  const [frqView, setFrqView] = useState<FrqViewEvent | null>(null)
  const [recList, setRecList] = useState<RecListEvent | null>(null)
  const [recIndex, setRecIndex] = useState<number | null>(null)
  const [recFilter, setRecFilter] = useState<"all" | "todo" | "done" | "extra">("all")
  const [overwriteWav, setOverwriteWav] = useState<string | null>(null)
  const [bank, setBank] = useState<BankEvent | null>(null)
  const [effects, setEffects] = useState<EffectsEvent | null>(null)
  const [noiseWave, setNoiseWave] = useState<NoiseWaveEvent | null>(null)
  const [guideFile, setGuideFile] = useState<GuideFileState>({ manual: true, path: "", label: "", recent: [] })
  const [takeWave, setTakeWave] = useState<{ name: string; lo: number[]; hi: number[]; duration: number } | null>(null)
  const [takePlay, setTakePlay] = useState({ playing: false, position: 0, duration: 0 })
  const [recentFolders, setRecentFolders] = useState<{ path: string; name: string }[]>([])
  const [createOpen, setCreateOpen] = useState(false)
  const [createName, setCreateName] = useState("")
  const [createPlace, setCreatePlace] = useState("")
  const [createError, setCreateError] = useState("")
  const [creating, setCreating] = useState(false)
  const createOpenRef = useRef(false)
  const [templates, setTemplates] = useState<string[]>([])
  const [plugins, setPlugins] = useState<PluginItem[]>([])
  const pluginPorts = useRef(new Map<string, (reply: PluginReply) => void>())
  const bindPlugin = useCallback((id: string, send: ((reply: PluginReply) => void) | null) => {
    if (send)
      pluginPorts.current.set(id, send)
    else
      pluginPorts.current.delete(id)
  }, [])
  const [updatePrompt, setUpdatePrompt] = useState<{ phase: string; title: string; body: string } | null>(null)
  const [frqMode, setFrqMode] = useState<FrqRadios["mode"]>("all")
  const [frqPreutter, setFrqPreutter] = useState<FrqRadios["preutter"]>("keep")
  const [canUndo, setCanUndo] = useState(false)
  const [canRedo, setCanRedo] = useState(false)
  const layoutTouched = useRef(false)
  const acceptLayout = useRef(false)
  const keepRatios = useRef(false)
  const accentCustom = useRef(false)
  const recTouched = useRef(false)
  const recListRef = useRef<RecListEvent | null>(null)
  const recIndexRef = useRef<number | null>(null)
  const advanceRef = useRef(false)
  const recordNameRef = useRef<string | null>(null)
  const historyRef = useRef<History>(emptyHistory())
  const batchRef = useRef<PendingEdit[]>([])
  const batchQueued = useRef(false)
  const replaying = useRef(false)
  const suppressRec = useRef(false)
  const guidePick = useRef(false)
  const recPick = useRef(false)
  const replayRef = useRef<(direction: "undo" | "redo") => void>(() => undefined)
  const snapRef = useRef<EditSnapshot | null>(null)
  if (!snapRef.current) {
    snapRef.current = {
      ...emptySnapshot(),
      appearance: { theme, accent: null, scale, lockPanels },
      monitor,
      rec: { note, semitones, speed, volume, countIn, countBeats, countBeatsAfter, bpm, moras, advance, recStartAfter, recEndBefore },
    }
  }
  const resolved = resolveTheme(theme, systemDark)
  const accentPaint = buttonColors(correctAccent(accent ?? defaultAccent(resolved), resolved), resolved)
  document.documentElement.dataset.theme = resolved
  document.documentElement.style.setProperty("--accent", accentPaint.fill)
  document.documentElement.style.setProperty("--on-accent", accentPaint.ink)
  pictureRef.current = picture
  otoRef.current = oto
  fileNameRef.current = fileName
  folderRef.current = folder
  namesRef.current = names
  recListRef.current = recList
  recIndexRef.current = recIndex
  advanceRef.current = advance
  createOpenRef.current = createOpen
  modelRef.current = viewModel()

  const register = useCallback((handle: BandHandle) => {
    bands.current.add(handle)
    return () => bands.current.delete(handle)
  }, [])

  useEffect(() => {
    const connection = connectHost((event) => applyRef.current(event))
    rawSend.current = connection.send
    setConnected(window.chrome?.webview != null)
    connection.send({ type: "hello" })
    return () => {
      window.clearTimeout(previewTimer.current)
      connection.close()
    }
  }, [])

  useEffect(() => {
    const focus = (event: FocusEvent) => {
      const target = event.target
      if (!isTextControl(target) || target.dataset.history != null)
        return
      target.dataset.history = target.value
    }
    window.addEventListener("focusin", focus)
    return () => window.removeEventListener("focusin", focus)
  }, [])

  useEffect(() => {
    const media = window.matchMedia("(prefers-color-scheme: dark)")
    const apply = () => setSystemDark(media.matches)
    media.addEventListener("change", apply)
    return () => media.removeEventListener("change", apply)
  }, [])

  useEffect(() => {
    sendRef.current({ type: "caption", theme: resolved })
  }, [resolved])

  useEffect(() => {
    const blockZoom = (event: WheelEvent) => {
      if (event.ctrlKey)
        event.preventDefault()
    }
    window.addEventListener("wheel", blockZoom, { capture: true, passive: false })
    return () => window.removeEventListener("wheel", blockZoom, { capture: true })
  }, [])

  useEffect(() => {
    const keys = (event: KeyboardEvent) => {
      if (event.ctrlKey && !event.altKey && !event.shiftKey && (event.key.toLowerCase() === "z" || event.key.toLowerCase() === "y")) {
        const target = event.target
        const text = isTextControl(target)
        if (textFieldOwnsUndo(text, text ? target.value : "", text ? target.dataset.history ?? null : null))
          return
        event.preventDefault()
        replayRef.current(event.key.toLowerCase() === "z" ? "undo" : "redo")
        return
      }
      if (event.ctrlKey && event.key.toLowerCase() === "o") {
        event.preventDefault()
        sendRef.current({ type: "openFolder" })
      }
      if (event.ctrlKey && event.key.toLowerCase() === "s") {
        event.preventDefault()
        if (oto?.analyzed && !oto.busy)
          sendRef.current({ type: "oto", op: "write" })
      }
      if (event.key === "Escape" && focusRef.current >= 0) {
        const target = event.target
        if (target instanceof HTMLElement && target.closest("input, textarea, select"))
          return
        pendingFocus.current = null
        liveRef.current = null
        draftRef.current = null
        setFocus(-1)
      }
    }
    window.addEventListener("keydown", keys)
    return () => window.removeEventListener("keydown", keys)
  }, [oto])

  useEffect(() => {
    sendRef.current({ type: "watchMic", on: contains(layout, "rec") || contains(layout, "mic") })
    sendRef.current({ type: "watchPanels", frq: contains(layout, "frqEdit"), oto: contains(layout, "otoParams") || contains(layout, "aliasList") })
  }, [layout])

  useEffect(() => {
    if (!fileName || missingRef.current || stepTarget.current != null)
      return
    const frame = window.requestAnimationFrame(() => sendPreview(rangeRef.current))
    return () => window.cancelAnimationFrame(frame)
  }, [fileName])

  useEffect(() => {
    if (suppressRec.current) {
      suppressRec.current = false
      return
    }
    if (!recTouched.current)
      return
    const timer = window.setTimeout(() => {
      sendRef.current({
        type: "recSettings",
        note,
        semitones,
        speed,
        volume,
        countIn,
        countBeats,
        countBeatsAfter,
        bpm,
        moras,
        advance,
        recStartAfter,
        recEndBefore,
      })
    }, 300)
    return () => window.clearTimeout(timer)
  }, [advance, bpm, countBeats, countBeatsAfter, countIn, moras, note, recEndBefore, recStartAfter, semitones, speed, volume])

  function viewModel(over: Partial<GraphModel> = {}): GraphModel {
    return {
      picture: pictureRef.current,
      range: rangeRef.current,
      marks: marksRef.current,
      spans: spansRef.current,
      note: noteRef.current,
      focus: focusRef.current,
      live: liveRef.current,
      draft: draftRef.current,
      ...over,
    }
  }

  function redraw() {
    for (const band of bands.current)
      band.redraw()
  }

  function setFocus(index: number) {
    if (focusRef.current === index) {
      setFocusIndex(index)
      return
    }
    focusRef.current = index
    setFocusIndex(index)
    modelRef.current = viewModel()
    redraw()
  }

  function sendPreview(range: Range) {
    if (!fileNameRef.current || missingRef.current)
      return
    const spec = [...bands.current].find((band) => band.kind === "spec")
    const measured = spec?.pixelSize() ?? { columns: 800, rows: 256 }
    const columns = Math.min(previewColumnLimit, Math.max(32, measured.columns))
    const kept = pictureCache.current.get(fileNameRef.current)
    if (kept && previewStillCovers(kept.picture, kept.columns, range, columns))
      return
    previewAsk.current = { t0: range.t0, t1: range.t1, columns }
    sendRef.current({
      type: "preview",
      t0: range.t0,
      t1: range.t1,
      columns,
      rows: Math.min(previewRowLimit, Math.max(32, measured.rows)),
    })
  }

  function schedulePreview() {
    window.clearTimeout(previewTimer.current)
    previewTimer.current = window.setTimeout(() => sendPreview(rangeRef.current), 180)
  }

  function onRange(range: Range, commit: boolean) {
    rangeRef.current = range
    modelRef.current = viewModel()
    redraw()
    if (!requestsPreviewAfter(commit ? "pan" : "zoom"))
      return
    if (commit) {
      window.clearTimeout(previewTimer.current)
      sendPreview(range)
      return
    }
    schedulePreview()
  }

  function showWheelLoading() {
    if (!wheeling.current) {
      wheeling.current = true
      wheelHold.current = {
        picture: pictureRef.current,
        note: noteRef.current,
        range: { ...rangeRef.current },
        pictureName: pictureName.current,
      }
    }
    if (pictureRef.current == null && noteRef.current === "読み込み中")
      return
    pictureName.current = ""
    noteRef.current = "読み込み中"
    setPicture(null)
    pictureRef.current = null
    rangeRef.current = { t0: 0, t1: 0 }
    pendingFocus.current = null
    focusRef.current = -1
    setFocusIndex(-1)
    liveRef.current = null
    draftRef.current = null
    modelRef.current = viewModel()
    redraw()
  }

  function restoreWheelHold() {
    const held = wheelHold.current
    wheeling.current = false
    wheelHold.current = null
    if (!held)
      return
    pictureName.current = held.pictureName
    noteRef.current = held.note
    pictureRef.current = held.picture
    rangeRef.current = held.range
    setPicture(held.picture)
    modelRef.current = viewModel()
    redraw()
  }

  function showFilePending(name: string) {
    loadingFile.current = name
    const cached = pictureCache.current.get(name)
    if (cached) {
      pictureName.current = name
      noteRef.current = ""
      setPicture(cached.picture)
      pictureRef.current = cached.picture
      rangeRef.current = { t0: cached.picture.waveT0, t1: cached.picture.waveT1 }
    } else {
      pictureName.current = ""
      noteRef.current = "読み込み中"
      setPicture(null)
      pictureRef.current = null
      rangeRef.current = { t0: 0, t1: 0 }
    }
    focusRef.current = -1
    setFocusIndex(-1)
    liveRef.current = null
    draftRef.current = null
    modelRef.current = viewModel()
    redraw()
  }

  function commitPick(next: FilePick) {
    pickRef.current = next
    setPick(next)
  }

  function chooseFile(name: string) {
    window.clearTimeout(fileStepTimer.current)
    stepTarget.current = null
    fileListRef.current?.highlight(name)
    if (!sameName(name, fileNameRef.current)) {
      wheeling.current = false
      wheelHold.current = null
      showFilePending(name)
    } else if (wheeling.current)
      restoreWheelHold()
    sendRef.current({ type: "selectFile", name })
  }

  function focusFile(name: string) {
    const listed = namesRef.current.some((item) => sameName(item, name))
    commitPick(listed
      ? { selected: [name], active: name, anchor: name }
      : { selected: [], active: name, anchor: name })
    chooseFile(name)
  }

  function clickFile(name: string, shift: boolean, ctrl: boolean) {
    pendingFocus.current = null
    const next = applyFileClick(namesRef.current, pickRef.current, name, shift, ctrl)
    const changed = !sameName(next.active, pickRef.current.active)
    commitPick(next)
    if (changed)
      chooseFile(next.active)
    else {
      fileListRef.current?.release()
      aliasListRef.current?.release()
    }
  }

  function selectAllFiles() {
    const list = namesRef.current
    if (list.length === 0)
      return
    const active = list.find((name) => sameName(name, pickRef.current.active)) ?? ""
    const anchor = list.find((name) => sameName(name, pickRef.current.anchor)) ?? active
    commitPick({ selected: [...list], active: active || list[0] || "", anchor: anchor || list[0] || "" })
    if (!active && list[0])
      chooseFile(list[0])
    else {
      fileListRef.current?.release()
      aliasListRef.current?.release()
    }
  }

  function applyAlias(row: OtoMark) {
    pendingFocus.current = { file: row.file, index: row.index, alias: row.alias, autoAlias: row.autoAlias, mode: row.mode }
    const mode = aliasPanelMode(row.mode)
    const modeChanged = mode != null && mode !== otoRef.current?.mode
    if (modeChanged)
      sendRef.current({ type: "oto", op: "mode", mode })
    if (!sameName(row.file, fileNameRef.current) || modeChanged)
      return
    setFocus(markIndexForAlias(row, marksRef.current, otoRef.current?.mode))
  }

  function clickAlias(id: string, shift: boolean, ctrl: boolean) {
    const row = findAlias(otoRef.current?.rows ?? [], id)
    if (!row)
      return
    clickFile(row.file, shift, ctrl)
    if (!sameName(pickRef.current.active, row.file))
      return
    applyAlias(row)
  }

  function stepGraph(direction: number) {
    if (contains(layout, "aliasList"))
      stepAlias(direction)
    else
      stepFile(direction)
  }

  function stepAlias(direction: number) {
    const rows = otoRef.current?.rows ?? []
    if (rows.length === 0 || direction === 0)
      return
    const index = aliasStepTarget.current ?? aliasStepIndex(rows, fileNameRef.current, focusRef.current, otoRef.current?.mode)
    const next = nextFileIndex(index, rows.length, direction)
    if (next < 0 || next === index)
      return
    const row = rows[next]
    if (!row)
      return
    aliasStepTarget.current = next
    stepTarget.current = null
    commitPick({ selected: [row.file], active: row.file, anchor: row.file })
    fileListRef.current?.highlight(row.file)
    aliasListRef.current?.highlight(aliasId(row))
    if (!sameName(row.file, fileNameRef.current))
      showWheelLoading()
    window.clearTimeout(fileStepTimer.current)
    fileStepTimer.current = window.setTimeout(() => {
      const landed = aliasStepTarget.current
      const chosen = landed == null ? null : (otoRef.current?.rows ?? [])[landed] ?? null
      wheeling.current = false
      if (!chosen) {
        aliasStepTarget.current = null
        fileListRef.current?.release()
        aliasListRef.current?.release()
        restoreWheelHold()
        return
      }
      const waiting = !sameName(chosen.file, fileNameRef.current)
        || (aliasPanelMode(chosen.mode) != null && aliasPanelMode(chosen.mode) !== otoRef.current?.mode)
      if (!waiting)
        aliasStepTarget.current = null
      applyAlias(chosen)
      if (!sameName(chosen.file, fileNameRef.current))
        focusFile(chosen.file)
      else {
        fileListRef.current?.release()
        aliasListRef.current?.release()
        restoreWheelHold()
      }
    }, 200)
  }

  function stepFile(direction: number) {
    const list = namesRef.current
    if (list.length === 0 || direction === 0)
      return
    const shown = stepTarget.current == null ? fileNameRef.current : list[stepTarget.current] ?? ""
    const index = list.findIndex((name) => sameName(name, shown))
    const next = nextFileIndex(stepTarget.current ?? index, list.length, direction)
    if (next < 0 || next === (stepTarget.current ?? index))
      return
    const name = list[next]
    if (!name)
      return
    stepTarget.current = next
    aliasStepTarget.current = null
    commitPick({ selected: [name], active: name, anchor: name })
    fileListRef.current?.highlight(name)
    aliasListRef.current?.release()
    showWheelLoading()
    window.clearTimeout(fileStepTimer.current)
    fileStepTimer.current = window.setTimeout(() => {
      const landed = stepTarget.current
      const chosen = landed == null ? "" : namesRef.current[landed] ?? ""
      wheeling.current = false
      if (chosen && !sameName(chosen, fileNameRef.current))
        focusFile(chosen)
      else {
        stepTarget.current = null
        fileListRef.current?.release()
        aliasListRef.current?.release()
        restoreWheelHold()
      }
    }, 200)
  }

  function applyHost(event: HostEvent) {
    switch (event.type) {
      case "devices":
        setInputs(event.inputs)
        setOutputs(event.outputs)
        break
      case "files": {
        const keep = missingRef.current || event.names.some((name) => sameName(name, fileNameRef.current))
        setCreateOpen(false)
        setCreating(false)
        const folderChanged = event.folder !== folderRef.current
        if (folderChanged) {
          pictureCache.current.clear()
          otoGenRef.current = -1
          batchRef.current = []
          historyRef.current = emptyHistory()
          guidePick.current = false
          recPick.current = false
          setCanUndo(false)
          setCanRedo(false)
          snapRef.current = {
            ...(snapRef.current ?? emptySnapshot()),
            oto: null,
            marks: [],
            spans: [],
            bank: null,
            guide: { manual: true, path: "" },
            recListPath: "",
          }
          if (otoRef.current) {
            const cleared = { ...otoRef.current, rows: [], marks: [], spans: [] }
            otoRef.current = cleared
            setOto(cleared)
          }
        }
        setFolder(event.folder)
        setNames(event.names)
        if (folderChanged)
          commitPick(keep ? { selected: [fileNameRef.current], active: fileNameRef.current, anchor: fileNameRef.current } : emptyFilePick)
        else if (!keep)
          commitPick(emptyFilePick)
        else
          commitPick(pruneFilePick(event.names, pickRef.current))
        window.clearTimeout(fileStepTimer.current)
        if (wheeling.current)
          restoreWheelHold()
        stepTarget.current = null
        aliasStepTarget.current = null
        fileListRef.current?.release()
        aliasListRef.current?.release()
        if (!keep) {
          fileNameRef.current = ""
          setFileName("")
          setPicture(null)
          pictureRef.current = null
          rangeRef.current = { t0: 0, t1: 0 }
          noteRef.current = ""
          pendingFocus.current = null
          focusRef.current = -1
          setFocusIndex(-1)
          liveRef.current = null
          draftRef.current = null
          modelRef.current = viewModel()
          redraw()
        }
        break
      }
      case "opened": {
        if (loadingFile.current != null && !sameName(loadingFile.current, event.name))
          break
        const pendingName = stepTarget.current == null ? null : namesRef.current[stepTarget.current]
        if (pendingName != null && !sameName(pendingName, event.name))
          break
        fileNameRef.current = event.name
        missingRef.current = event.missing === true
        setFileName(event.name)
        if (!sameName(pickRef.current.active, event.name)) {
          const listed = namesRef.current.some((item) => sameName(item, event.name))
          commitPick(listed
            ? { selected: [event.name], active: event.name, anchor: event.name }
            : { selected: [], active: event.name, anchor: event.name })
        }
        stepTarget.current = null
        fileListRef.current?.release()
        aliasListRef.current?.release()
        if (event.missing) {
          loadingFile.current = null
          pictureCache.current.delete(event.name)
          pictureName.current = ""
          noteRef.current = "このファイルは存在しません"
          setPicture(null)
          pictureRef.current = null
          rangeRef.current = { t0: 0, t1: 0 }
          pendingFocus.current = null
          focusRef.current = -1
          setFocusIndex(-1)
          liveRef.current = null
          draftRef.current = null
          modelRef.current = viewModel()
          redraw()
        } else {
          if (!sameName(pictureName.current, event.name) || pictureRef.current == null)
            showFilePending(event.name)
          loadingFile.current = null
        }
        window.clearTimeout(previewTimer.current)
        syncRecIndex(event.name)
        break
      }
      case "preview": {
        if (event.name !== "" && !sameName(event.name, fileNameRef.current))
          break
        if (stepTarget.current != null || wheeling.current)
          break
        if (loadingFile.current != null && !sameName(loadingFile.current, fileNameRef.current))
          break
        missingRef.current = false
        const sameFile = pictureName.current !== "" && pictureName.current === fileNameRef.current
        const sameLength = pictureRef.current == null || event.duration === 0 || event.duration === pictureRef.current.duration
        const keepSpec = sameFile && sameLength
        const next = mergePreview(pictureRef.current, event, keepSpec)
        if (next.specColumns > 0 && next.spec.length > 0)
          noteRef.current = ""
        pictureName.current = fileNameRef.current
        const cacheName = event.name || fileNameRef.current
        if (cacheName)
          rememberPicture(pictureCache.current, cacheName, next, previewAsk.current.columns)
        setPicture(next)
        pictureRef.current = next
        if (event.lo.length > 0)
          rangeRef.current = { t0: event.waveT0, t1: event.waveT1 }
        modelRef.current = viewModel()
        redraw()
        break
      }
      case "capture":
        bus.show(event)
        break
      case "guide":
        setGuidePlaying(event.playing)
        break
      case "guideFile": {
        const snap = snapRef.current ?? emptySnapshot()
        const previous = snap.guide
        const picked = guidePick.current
        guidePick.current = false
        snapRef.current = { ...snap, guide: { manual: event.manual, path: event.path } }
        if (picked && (previous.manual !== event.manual || previous.path !== event.path)) {
          const undo: PageCommand[] = []
          const redo: PageCommand[] = []
          if (previous.manual !== event.manual) {
            undo.push({ type: "guideFile", manual: previous.manual })
            redo.push({ type: "guideFile", manual: event.manual })
          }
          if (previous.path !== event.path) {
            undo.push({ type: "guideFile", path: previous.path })
            redo.push({ type: "guideFile", path: event.path })
          }
          historyRef.current = pushStep(historyRef.current, { key: "guide.pick", coalesce: false, at: Date.now(), undo, redo })
          setCanUndo(historyRef.current.undo.length > 0)
          setCanRedo(historyRef.current.redo.length > 0)
        }
        setGuideFile({ manual: event.manual, path: event.path, label: event.label, recent: event.recent })
        break
      }
      case "take":
        setTakePlay({ playing: event.playing, position: event.position, duration: event.duration })
        break
      case "takeWave":
        setTakeWave(event)
        break
      case "noiseWave":
        setNoiseWave(event.file ? event : null)
        break
      case "recent":
        setRecentFolders(event.folders)
        break
      case "projectPlace":
        setCreatePlace(event.path)
        setCreateError("")
        break
      case "templates":
        setTemplates(event.names)
        break
      case "appearance":
        snapRef.current = {
          ...(snapRef.current ?? emptySnapshot()),
          appearance: { theme: event.theme, accent: event.accent, scale: event.scale, lockPanels: event.lockPanels },
        }
        setTheme(event.theme)
        setScale(event.scale)
        applyPageScale(event.scale)
        setLockPanels(event.lockPanels)
        applyPanelsLocked(event.lockPanels)
        if (event.accent) {
          accentCustom.current = true
          setAccent(event.accent)
        } else {
          accentCustom.current = false
          setAccent(null)
        }
        break
      case "update":
        setUpdatePrompt({ phase: event.phase, title: event.title, body: event.body })
        break
      case "saved":
        setSaved(event)
        setNotice({ message: `${event.path} を書きました。入力 ${event.inputRate} Hz、ヘッダ ${event.headerRate} Hz / ${event.bits} bit / ${event.channels} ch。`, tone: "success" })
        advanceAfterSave()
        break
      case "failed":
        setCreating(false)
        if (createOpenRef.current) {
          setCreateError(event.message)
          break
        }
        setNotice({ message: event.message, tone: "error" })
        break
      case "notice":
        setNotice({ message: event.message, tone: noticeTone(event.message) })
        break
      case "status":
        setStatus(event.message)
        break
      case "recordWait":
        setWaiting(event.waiting)
        break
      case "otoExists":
        setReplaceName(event.name)
        break
      case "oto": {
        if (event.generation < otoGenRef.current)
          break
        otoGenRef.current = event.generation
        const rows = event.rows ?? otoRef.current?.rows ?? []
        const next = { ...event, rows }
        snapRef.current = absorbOto(snapRef.current ?? emptySnapshot(), event)
        otoRef.current = next
        marksRef.current = next.marks
        spansRef.current = next.spans
        let nextFocus = focusRef.current
        const pending = pendingFocus.current
        if (pending && pendingAliasReady(pending, fileNameRef.current, next.mode)) {
          const index = markIndexForAlias(pending, next.marks, next.mode)
          if (index >= 0 && next.marks.some((mark) => mark.index === index))
            nextFocus = index
          pendingFocus.current = null
          aliasStepTarget.current = null
        } else if (nextFocus >= 0 && !next.marks.some((mark) => mark.index === nextFocus))
          nextFocus = -1
        if (focusRef.current !== nextFocus) {
          focusRef.current = nextFocus
          setFocusIndex(nextFocus)
        }
        modelRef.current = viewModel()
        setOto(next)
        for (const band of bands.current)
          band.redraw()
        break
      }
      case "frq":
        setFrqCount({ wavs: event.wavs, frqs: event.frqs, busy: event.busy })
        break
      case "frqView":
        setFrqView(event)
        break
      case "recList": {
        const snap = snapRef.current ?? emptySnapshot()
        const previous = snap.recListPath
        const picked = recPick.current
        recPick.current = false
        snapRef.current = { ...snap, recListPath: event.path }
        if (picked && previous !== event.path) {
          const undo: PageCommand = previous.length === 0
            ? { type: "recList", op: "none" }
            : { type: "recList", op: "open", path: previous }
          const redo: PageCommand = event.path.length === 0
            ? { type: "recList", op: "none" }
            : { type: "recList", op: "open", path: event.path }
          historyRef.current = pushStep(historyRef.current, { key: "recList.pick", coalesce: false, at: Date.now(), undo: [undo], redo: [redo] })
          setCanUndo(historyRef.current.undo.length > 0)
          setCanRedo(historyRef.current.redo.length > 0)
        }
        setRecList(event)
        setRecIndex((index) => {
          const previous = recListRef.current?.lines[index ?? -1]
          if (!previous)
            return index != null && index < event.lines.length ? index : null
          const next = event.lines.findIndex((line) => sameName(line.wav, previous.wav))
          return next >= 0 ? next : null
        })
        break
      }
      case "bank":
        snapRef.current = absorbBank(snapRef.current ?? emptySnapshot(), event)
        setBank(event)
        break
      case "effects":
        snapRef.current = absorbEffects(snapRef.current ?? emptySnapshot(), event)
        setEffects(event)
        break
      case "recSettings":
        if (recTouched.current)
          break
        snapRef.current = {
          ...(snapRef.current ?? emptySnapshot()),
          rec: {
            note: event.note,
            semitones: event.semitones,
            speed: event.speed,
            volume: event.volume,
            countIn: event.countIn,
            countBeats: event.countBeats,
            countBeatsAfter: event.countBeatsAfter,
            bpm: event.bpm,
            moras: event.moras,
            advance: event.advance,
            recStartAfter: event.recStartAfter,
            recEndBefore: event.recEndBefore,
          },
        }
        setNote(event.note)
        setSemitones(event.semitones)
        setSpeed(event.speed)
        setVolume(event.volume)
        setCountIn(event.countIn)
        setCountBeats(event.countBeats)
        setCountBeatsAfter(event.countBeatsAfter)
        setBpm(event.bpm)
        setMoras(event.moras)
        setRecStartAfter(event.recStartAfter)
        setRecEndBefore(event.recEndBefore)
        setAdvance(event.advance)
        break
      case "layout":
        if (acceptLayout.current)
          acceptLayout.current = false
        else if (layoutTouched.current)
          break
        keepRatios.current = event.keepRatios
        setLayout(event.root ?? createDefault())
        break
      case "plugins":
        registerPlugins(event.items.map((item) => ({ id: pluginPaneId(item.id), title: item.title })))
        setPlugins(event.items)
        setLayout((current) => current ? parseLayout(current) ?? current : current)
        break
      case "plugin":
        pluginPorts.current.get(event.id)?.(event)
        break
    }
  }
  applyRef.current = applyHost

  function commitLayout(next: LayoutNode | null, keep = keepRatios.current) {
    layoutTouched.current = true
    keepRatios.current = keep
    setLayout(next)
    sendRef.current({ type: "layout", root: next, keepRatios: keep })
  }

  function applyTemplate(name: string) {
    acceptLayout.current = true
    sendRef.current({ type: "template", op: "apply", name })
  }

  function saveTemplate() {
    const name = window.prompt("配置テンプレートの名前")
    if (!name)
      return
    sendRef.current({ type: "template", op: "save", name: name.trim() })
  }

  function sendAppearance(nextTheme: ThemeChoice, nextAccent: string | null, nextScale = scale, nextLock = lockPanels) {
    sendRef.current({ type: "appearance", theme: nextTheme, accent: nextAccent, scale: nextScale, lockPanels: nextLock })
  }

  function chooseTheme(choice: ThemeChoice) {
    setTheme(choice)
    sendAppearance(choice, accentCustom.current ? accent : null)
  }

  function chooseAccent(value: string) {
    accentCustom.current = true
    setAccent(value)
    sendAppearance(theme, value)
  }

  function chooseScale(value: number) {
    const next = uiScale(value)
    setScale(next)
    applyPageScale(next)
    sendAppearance(theme, accentCustom.current ? accent : null, next)
  }

  function chooseLock(value: boolean) {
    setLockPanels(value)
    applyPanelsLocked(value)
    sendAppearance(theme, accentCustom.current ? accent : null, scale, value)
  }

  function togglePane(id: string) {
    commitLayout(contains(layout, id) ? hidePane(layout, id) : showPane(layout, id, boxes.current))
  }

  function touchRec<T>(apply: (value: T) => void) {
    return (value: T) => {
      recTouched.current = true
      apply(value)
    }
  }

  function chooseRec(index: number | null, wav: string) {
    setRecIndex(index)
    if (wav)
      focusFile(wav)
  }

  function syncRecIndex(wav: string) {
    const lines = recListRef.current?.lines ?? []
    const index = lines.findIndex((line) => sameName(line.wav, wav))
    setRecIndex(index >= 0 ? index : null)
  }

  function nextUnrecorded(from: number) {
    const lines = recListRef.current?.lines ?? []
    for (let i = from + 1; i < lines.length; i++) {
      const line = lines[i]
      if (line && !line.error && !line.recorded)
        return i
    }
    for (let i = 0; i < from; i++) {
      const line = lines[i]
      if (line && !line.error && !line.recorded)
        return i
    }
    return -1
  }

  function advanceAfterSave() {
    const name = recordNameRef.current
    recordNameRef.current = null
    if (!advanceRef.current || !name)
      return
    const lines = recListRef.current?.lines ?? []
    const index = lines.findIndex((line) => line.sing === name)
    const next = nextUnrecorded(index)
    const line = lines[next]
    if (line)
      chooseRec(next, line.wav)
  }

  function sendRecord(name?: string) {
    recordNameRef.current = name ?? null
    sendRef.current({
      type: "record",
      name,
      countIn,
      countBeats,
      countBeatsAfter,
      bpm,
      moras,
      volume,
      note,
      semitones,
      speed,
      recStartAfter,
      recEndBefore,
    })
  }

  function moveRec(direction: number) {
    const lines = recListRef.current?.lines ?? []
    if (lines.length === 0)
      return
    const current = recIndexRef.current ?? (direction > 0 ? -1 : lines.length)
    const next = Math.min(lines.length - 1, Math.max(0, current + direction))
    const line = lines[next]
    if (line)
      chooseRec(next, line.wav)
  }

  function moveMissing() {
    const next = nextUnrecorded(recIndexRef.current ?? -1)
    const line = recListRef.current?.lines[next]
    if (line)
      chooseRec(next, line.wav)
  }

  function startRecord(stop: boolean) {
    if (stop) {
      recordNameRef.current = null
      sendRef.current({ type: "recordStop" })
      return
    }
    const line = recIndexRef.current == null ? null : recListRef.current?.lines[recIndexRef.current] ?? null
    if (line?.error) {
      setNotice({ message: line.error, tone: "error" })
      return
    }
    if (line?.recorded) {
      setOverwriteWav(line.wav)
      return
    }
    sendRecord(line?.sing)
  }

  function rememberFlags() {
    setCanUndo(historyRef.current.undo.length > 0)
    setCanRedo(historyRef.current.redo.length > 0)
  }

  function flushBatch() {
    batchQueued.current = false
    const edits = batchRef.current
    batchRef.current = []
    const step = groupEdits(edits, Date.now())
    if (!step)
      return
    historyRef.current = pushStep(historyRef.current, step)
    rememberFlags()
    stampActiveText()
  }

  function queueBatch() {
    if (batchQueued.current)
      return
    batchQueued.current = true
    queueMicrotask(flushBatch)
  }

  function track(command: PageCommand) {
    if (clearsHistory(command)) {
      batchRef.current = []
      batchQueued.current = false
      historyRef.current = emptyHistory()
      guidePick.current = false
      recPick.current = false
      rememberFlags()
      rawSend.current(command)
      return
    }
    if (command.type === "guideFile")
      guidePick.current = command.pick === true
    if (command.type === "recList")
      recPick.current = command.op === "pick"
    const snap = snapRef.current ?? emptySnapshot()
    if (replaying.current)
      snapRef.current = applySnapshot(snap, command)
    else {
      const edit = describeEdit(command, snap)
      if (edit) {
        batchRef.current.push(edit)
        snapRef.current = applySnapshot(snap, command)
        queueBatch()
      }
    }
    rawSend.current(command)
  }

  function publishEdit(commands: PageCommand[]) {
    const snap = snapRef.current ?? emptySnapshot()
    if (commands.some((command) => command.type === "oto") && snap.oto) {
      otoRef.current = snap.oto
      marksRef.current = snap.marks
      spansRef.current = snap.spans
      setOto(snap.oto)
      modelRef.current = viewModel()
      redraw()
    }
    if (commands.some((command) => command.type === "effects") && snap.effects)
      setEffects(snap.effects)
    if (commands.some((command) => command.type === "bank") && snap.bank)
      setBank((current) => current ? { ...current, ...snap.bank, type: "bank" } : current)
    if (commands.some((command) => command.type === "appearance")) {
      const next = snap.appearance
      setTheme(next.theme)
      setScale(next.scale)
      applyPageScale(next.scale)
      setLockPanels(next.lockPanels)
      applyPanelsLocked(next.lockPanels)
      accentCustom.current = next.accent != null
      setAccent(next.accent)
    }
    if (commands.some((command) => command.type === "setDevices")) {
      setInputId(snap.devices.inputId)
      setOutputId(snap.devices.outputId)
    }
    if (commands.some((command) => command.type === "monitor"))
      setMonitor(snap.monitor)
    if (commands.some((command) => command.type === "recSettings")) {
      const rec = snap.rec
      const changed = note !== rec.note
        || semitones !== rec.semitones
        || speed !== rec.speed
        || volume !== rec.volume
        || countIn !== rec.countIn
        || countBeats !== rec.countBeats
        || countBeatsAfter !== rec.countBeatsAfter
        || bpm !== rec.bpm
        || moras !== rec.moras
        || advance !== rec.advance
      if (changed) {
        suppressRec.current = true
        setNote(rec.note)
        setSemitones(rec.semitones)
        setSpeed(rec.speed)
        setVolume(rec.volume)
        setCountIn(rec.countIn)
        setCountBeats(rec.countBeats)
        setCountBeatsAfter(rec.countBeatsAfter)
        setBpm(rec.bpm)
        setMoras(rec.moras)
        setAdvance(rec.advance)
      }
    }
    if (commands.some((command) => command.type === "guideFile"))
      setGuideFile((current) => ({ ...current, manual: snap.guide.manual, path: snap.guide.path }))
    if (commands.some((command) => command.type === "recList"))
      setRecList((current) => current ? { ...current, path: snap.recListPath } : current)
  }

  function replay(direction: "undo" | "redo") {
    if (batchQueued.current)
      flushBatch()
    const result = direction === "undo" ? undoHistory(historyRef.current) : redoHistory(historyRef.current)
    if (!result)
      return
    historyRef.current = result.history
    rememberFlags()
    const commands = direction === "undo" ? result.step.undo : result.step.redo
    const radios = direction === "undo" ? result.step.undoFrq : result.step.redoFrq
    replaying.current = true
    for (const command of commands)
      track(command)
    replaying.current = false
    if (radios) {
      setFrqMode(radios.mode)
      setFrqPreutter(radios.preutter)
    }
    publishEdit(commands)
    window.requestAnimationFrame(stampActiveText)
  }

  function changeFrq(next: FrqRadios) {
    const step = frqStep({ mode: frqMode, preutter: frqPreutter }, next, Date.now())
    if (!step)
      return
    setFrqMode(next.mode)
    setFrqPreutter(next.preutter)
    if (batchQueued.current)
      flushBatch()
    historyRef.current = pushStep(historyRef.current, step)
    rememberFlags()
  }

  sendRef.current = track
  replayRef.current = replay

  const recLine = recIndex == null ? null : recList?.lines[recIndex] ?? null
  const selectedNames = names.filter((name) => pick.selected.some((item) => sameName(item, name)))
  const aliasRows = oto?.rows ?? []
  const aliasSelected = selectedAliasIds(aliasRows, pick.selected)
  const steppingAlias = aliasStepTarget.current
  const aliasCurrent = steppingAlias != null && aliasRows[steppingAlias]
    ? aliasId(aliasRows[steppingAlias]!)
    : currentAliasId(aliasRows, pick.active, focusIndex, oto?.mode)
  useEffect(() => {
    if (recLine?.recorded)
      sendRef.current({ type: "take", op: "wave", name: recLine.wav })
    else
      setTakeWave(null)
  }, [recLine?.recorded, recLine?.wav])
  const recNext = recLine && recIndex != null ? recList?.lines[recIndex + 1]?.sing ?? "" : ""
  const recOutside = !recLine && (recList?.outside.some((name) => sameName(name, fileName)) ?? false) ? fileName : ""
  const recStatus = recLine?.error ?? (recLine ? (recLine.recorded ? "録音済み" : "未録音") : recOutside ? "リスト外" : "")
  const appearance = `${resolved}-${accentPaint.fill}-${scale}`

  return (
    <div className="flex h-full min-h-0 flex-col bg-[var(--bg)] text-[var(--text)]">
      <MenuBar
        layout={layout}
        canEdit={folder.length > 0}
        canWrite={Boolean(oto?.analyzed && !oto.busy)}
        onOpenFolder={() => sendRef.current({ type: "openFolder" })}
        onSaveSettings={() => {
          setSaveName("")
          setSaveOpen(true)
        }}
        onLoadSettings={() => setLoadOpen(true)}
        onWrite={() => sendRef.current({ type: "oto", op: "write" })}
        canUndo={canUndo}
        canRedo={canRedo}
        onUndo={() => replay("undo")}
        onRedo={() => replay("redo")}
        onSettings={() => setSettings(true)}
        onClose={() => sendRef.current({ type: "close" })}
        onToggle={togglePane}
        onReset={() => applyTemplate("初期配置")}
        onRecording={() => applyTemplate("録音")}
        templates={templates}
        onTemplate={applyTemplate}
        onSaveTemplate={saveTemplate}
        onCheckUpdate={() => sendRef.current({ type: "update", op: "check" })}
        plugins={plugins}
      />
      {status && (
        <div role="status" className="flex items-center gap-2 border-b border-l-[3px] border-[var(--line)] bg-[var(--panel)] px-4 py-2 text-sm" style={{ borderLeftColor: "var(--muted)" }}>
          <ToneIcon tone="info" />
          <span className="min-w-0 flex-1">{status}</span>
        </div>
      )}
      {notice && (
        <div role={notice.tone === "error" ? "alert" : "status"} className="flex items-center gap-2 border-b border-l-[3px] border-[var(--line)] bg-[var(--panel)] px-4 py-2 text-sm" style={{ borderLeftColor: notice.tone === "success" ? "var(--success)" : notice.tone === "error" ? "var(--danger)" : "var(--muted)" }}>
          <ToneIcon tone={notice.tone} />
          <span className="min-w-0 flex-1">{notice.message}</span>
          <button type="button" className="ghost-button" onClick={() => setNotice(null)}>閉じる</button>
        </div>
      )}
      {folder ? (
        <div className="min-h-0 min-w-0 flex-1 overflow-hidden">
          <PaneSlots
            ids={layout ? idsOf(layout) : []}
            render={(id) => {
              if (id === "file")
                return (
                  <FileList
                    ref={fileListRef}
                    folder={folder}
                    names={names}
                    selected={pick.selected}
                    current={pick.active}
                    onSelect={clickFile}
                    onSelectAll={selectAllFiles}
                  />
                )
              if (id === "aliasList")
                return (
                  <AliasList
                    ref={aliasListRef}
                    folder={folder}
                    rows={aliasRows}
                    selected={aliasSelected}
                    current={aliasCurrent}
                    onSelect={clickAlias}
                    onSelectAll={selectAllFiles}
                  />
                )
              if (id in graphKinds)
                return (
                  <GraphBand
                    kind={graphKinds[id]!}
                    model={modelRef}
                    appearance={appearance}
                    pointerBus={pointerBus}
                    register={register}
                    onRange={onRange}
                    onFileStep={stepGraph}
                    onTune={(index, point, time) => {
                      const key = (["offset", "overlap", "preutter", "consonant", "blank"] as const)[point]
                      if (key)
                        marksRef.current = marksRef.current.map((mark) => mark.index === index ? { ...mark, [key]: time } : mark)
                      liveRef.current = null
                      modelRef.current = viewModel()
                      redraw()
                      sendRef.current({ type: "oto", op: "tune", file: fileNameRef.current, index, point, time })
                    }}
                    onFocus={setFocus}
                    onLive={(live) => {
                      liveRef.current = live
                      modelRef.current = viewModel()
                      redraw()
                    }}
                    onDraft={(spans) => {
                      draftRef.current = spans
                      modelRef.current = viewModel()
                      redraw()
                    }}
                    onSpans={(spans) => {
                      spansRef.current = spans
                      draftRef.current = null
                      modelRef.current = viewModel()
                      redraw()
                      sendRef.current({
                        type: "oto",
                        op: "spans",
                        file: fileNameRef.current,
                        spans: spans.map((span) => ({ start: span.start, end: span.end })),
                      })
                    }}
                  />
                )
              if (id === "rec")
                return (
                  <RecPanel
                    bus={bus}
                    saved={saved}
                    status={recStatus}
                    waiting={waiting}
                    advance={advance}
                    setAdvance={touchRec(setAdvance)}
                    onPrev={() => moveRec(-1)}
                    onNext={() => moveRec(1)}
                    onNextMissing={moveMissing}
                    onRecord={startRecord}
                    blocked={Boolean(recLine?.error)}
                  />
                )
              if (id === "guide")
                return (
                  <GuidePanel
                    guide={guideFile}
                    note={note}
                    semitones={semitones}
                    speed={speed}
                    volume={volume}
                    countIn={countIn}
                    countBeats={countBeats}
                    countBeatsAfter={countBeatsAfter}
                    bpm={bpm}
                    moras={moras}
                    recStartAfter={recStartAfter}
                    recEndBefore={recEndBefore}
                    playing={guidePlaying}
                    setNote={touchRec(setNote)}
                    setSemitones={touchRec(setSemitones)}
                    setSpeed={touchRec(setSpeed)}
                    setVolume={touchRec(setVolume)}
                    setCountIn={touchRec(setCountIn)}
                    setCountBeats={touchRec(setCountBeats)}
                    setCountBeatsAfter={touchRec(setCountBeatsAfter)}
                    setBpm={touchRec(setBpm)}
                    setMoras={touchRec(setMoras)}
                    setRecStartAfter={touchRec(setRecStartAfter)}
                    setRecEndBefore={touchRec(setRecEndBefore)}
                    send={(command) => sendRef.current(command)}
                  />
                )
              if (id === "recList")
                return (
                  <RecListPanel
                    list={recList}
                    filter={recFilter}
                    selected={recIndex}
                    setFilter={setRecFilter}
                    onChoose={() => sendRef.current({ type: "recList", op: "none" })}
                    onPick={() => sendRef.current({ type: "recList", op: "pick" })}
                    onOpen={(path) => sendRef.current({ type: "recList", op: "open", path })}
                    onSelect={chooseRec}
                    activeWav={fileName}
                  />
                )
              if (id === "recScript")
                return <RecScriptPanel line={recLine} next={recNext} outside={recOutside} />
              if (id === "frq")
                return (
                  <FrqPanel
                    wavs={frqCount.wavs}
                    busy={Boolean(oto?.busy) || frqCount.busy}
                    hasFile={selectedNames.length > 0}
                    selectedNames={selectedNames}
                    mode={frqMode}
                    preutter={frqPreutter}
                    onMode={(mode) => changeFrq({ mode, preutter: frqPreutter })}
                    onPreutter={(preutter) => changeFrq({ mode: frqMode, preutter })}
                    send={(command) => sendRef.current(command)}
                  />
                )
              if (id === "frqEdit")
                return <FrqEditor view={frqView} appearance={appearance} send={(command) => sendRef.current(command)} />
              if (id === "otoParams")
                return <OtoTable rows={oto?.rows?.length ? oto.rows : oto?.marks ?? []} send={(command) => sendRef.current(command)} />
              if (id === "mic")
                return (
                  <MicPanel
                    bus={bus}
                    appearance={appearance}
                    monitor={monitor}
                    setMonitor={setMonitor}
                    recorded={Boolean(recLine?.recorded)}
                    takeName={recLine?.wav ?? ""}
                    wave={takeWave && recLine && takeWave.name === recLine.wav ? takeWave : null}
                    playing={takePlay.playing}
                    position={takePlay.position}
                    send={(command) => sendRef.current(command)}
                  />
                )
              if (id === "source")
                return <EffectsPanel effects={effects} wave={noiseWave} appearance={appearance} busy={Boolean(oto?.busy)} send={(command) => sendRef.current(command)} />
              if (id === "bank")
                return <BankPanel bank={bank} send={(command) => sendRef.current(command)} />
              if (id === "analyze")
                return (
                  <AnalyzePanel
                    busy={Boolean(oto?.busy)}
                    hasSelection={selectedNames.length > 0}
                    onAll={() => setReanalyzeOpen(true)}
                    onSelected={() => setOneOpen(true)}
                  />
                )
              if (id === "oto")
                return <OtoSettings oto={oto} send={(command) => sendRef.current(command)} />
              if (id === "otoIni")
                return <OtoIni oto={oto} send={(command) => sendRef.current(command)} />
              if (id.startsWith("plugin.")) {
                const plugin = plugins.find((item) => pluginPaneId(item.id) === id)
                if (!plugin)
                  return null
                const pluginContext: PluginContext = {
                  theme: resolved,
                  accent: accentPaint.fill,
                  scale,
                  file: fileName,
                  selected: selectedNames,
                }
                return (
                  <PluginFrame
                    item={plugin}
                    context={pluginContext}
                    onCall={(command) => sendRef.current(command)}
                    bind={bindPlugin}
                  />
                )
              }
              return null
            }}
          >
            <Dock
              layout={layout}
              locked={lockPanels}
              boxes={boxes}
              onChange={(next, persist) => {
                if (persist)
                  commitLayout(next)
                else
                  setLayout(next)
              }}
              onRatio={(next) => commitLayout(next, true)}
              onResizing={(holding) => {
                for (const band of bands.current)
                  band.holdLayout(holding)
                if (!holding)
                  sendPreview(rangeRef.current)
              }}
            />
          </PaneSlots>
        </div>
      ) : (
        <main className="flex flex-1 flex-col items-center justify-center gap-2 px-6 text-center">
          <h1 className="text-2xl font-semibold">オトアソビ</h1>
          <p className="text-sm text-[var(--muted)]">OTOasobi</p>
          <div className="mt-2 flex flex-col gap-2">
            <button
              type="button"
              className="accent-button min-w-52"
              onClick={() => {
                setCreateName("")
                setCreateError("")
                setCreating(false)
                setCreateOpen(true)
              }}
            >
              新しいプロジェクトを作成する
            </button>
            <button type="button" className="accent-button min-w-52" onClick={() => sendRef.current({ type: "openFolder" })}>
              既存のフォルダを選ぶ
            </button>
          </div>
          {recentFolders.length > 0 && (
            <div className="mt-4 w-full max-w-md text-left text-sm">
              <p className="mb-1 text-[var(--muted)]">最近使ったフォルダ</p>
              {recentFolders.map((item) => (
                <button
                  key={item.path}
                  type="button"
                  className="block w-full truncate rounded px-2 py-1 text-left hover:bg-[var(--line)]"
                  onClick={() => sendRef.current({ type: "openRecent", path: item.path })}
                >
                  {item.name}
                </button>
              ))}
            </div>
          )}
          {!connected && <p className="text-sm text-[var(--muted)]">ホストに接続していません</p>}
        </main>
      )}
      {createOpen && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
          <form
            className="w-full max-w-md rounded-md border border-[var(--line)] bg-[var(--panel)] p-4 text-left"
            onSubmit={(event) => {
              event.preventDefault()
              if (creating || createName.trim().length === 0 || createPlace.length === 0)
                return
              if (!connected) {
                setCreateError("ホストに接続していません")
                return
              }
              setCreateError("")
              setCreating(true)
              sendRef.current({ type: "createProject", parent: createPlace, name: createName.trim() })
            }}
          >
            <h2 className="text-base font-semibold">新しいプロジェクト</h2>
            <label className="mt-3 block text-sm" htmlFor="project-name">名前</label>
            <input
              id="project-name"
              className="field mt-1 w-full"
              value={createName}
              placeholder="音源の名前"
              onChange={(event) => setCreateName(event.target.value)}
            />
            <label className="mt-3 block text-sm" htmlFor="project-place">場所</label>
            <div className="mt-1 flex gap-2">
              <input id="project-place" className="field min-w-0 flex-1" readOnly value={createPlace} placeholder="フォルダを選んでください" />
              <button type="button" className="ghost-button shrink-0" onClick={() => sendRef.current({ type: "pickProjectPlace" })}>
                場所を選ぶ
              </button>
            </div>
            {createError && <ToneRow tone="error">{createError}</ToneRow>}
            <div className="mt-4 flex gap-2">
              <button
                type="button"
                className="ghost-button flex-1"
                onClick={() => {
                  setCreating(false)
                  setCreateOpen(false)
                }}
              >
                キャンセル
              </button>
              <button type="submit" className="accent-button flex-1" disabled={creating || createName.trim().length === 0 || createPlace.length === 0}>
                作成
              </button>
            </div>
          </form>
        </div>
      )}
      {saveOpen && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
          <form
            className="w-full max-w-md rounded-md border border-[var(--line)] bg-[var(--panel)] p-4"
            onSubmit={(event) => {
              event.preventDefault()
              sendRef.current({ type: "saveSettings", name: saveName, replace: false })
              setSaveOpen(false)
            }}
          >
            <h2 className="text-base font-semibold">設定を保存</h2>
            <input className="field mt-3 w-full" value={saveName} placeholder="設定名" onChange={(event) => setSaveName(event.target.value)} />
            <div className="mt-4 flex gap-2">
              <button type="button" className="ghost-button flex-1" onClick={() => setSaveOpen(false)}>キャンセル</button>
              <button type="submit" className="accent-button flex-1">保存</button>
            </div>
          </form>
        </div>
      )}
      {replaceName && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
          <div className="w-full max-w-md rounded-md border border-[var(--line)] bg-[var(--panel)] p-4">
            <h2 className="text-base font-semibold">設定を上書きしますか？</h2>
            <p className="mt-2 text-sm">「{replaceName}」は既にあります。いまの設定で置き換えます。</p>
            <div className="mt-4 flex gap-2">
              <button type="button" className="ghost-button flex-1" onClick={() => setReplaceName(null)}>キャンセル</button>
              <button
                type="button"
                className="accent-button flex-1"
                onClick={() => {
                  sendRef.current({ type: "saveSettings", name: replaceName, replace: true })
                  setReplaceName(null)
                }}
              >
                上書きする
              </button>
            </div>
          </div>
        </div>
      )}
      {loadOpen && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
          <div className="w-full max-w-md rounded-md border border-[var(--line)] bg-[var(--panel)] p-4">
            <h2 className="text-base font-semibold">設定を読み込む</h2>
            {oto && oto.saves.length > 0 ? (
              <div className="mt-3 grid max-h-64 gap-1 overflow-auto">
                {oto.saves.map((item) => (
                  <button
                    key={item.name}
                    type="button"
                    className="ghost-button text-left"
                    onClick={() => {
                      sendRef.current({ type: "loadSettings", name: item.name })
                      setLoadOpen(false)
                    }}
                  >
                    {item.name}　{item.mode}
                  </button>
                ))}
              </div>
            ) : null}
            <button type="button" className="ghost-button mt-4 w-full" onClick={() => setLoadOpen(false)}>閉じる</button>
          </div>
        </div>
      )}
      {reanalyzeOpen && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
          <div className="w-full max-w-md rounded-md border border-[var(--line)] bg-[var(--panel)] p-4">
            <h2 className="text-base font-semibold">すべてのデータを解析します</h2>
            <p className="mt-2 text-sm">フォルダ内の WAV をすべて解析します。</p>
            <div className="mt-4 flex gap-2">
              <button type="button" className="ghost-button flex-1" onClick={() => setReanalyzeOpen(false)}>いいえ</button>
              <button
                type="button"
                className="accent-button flex-1"
                onClick={() => {
                  sendRef.current({ type: "oto", op: "analyze", force: true })
                  setReanalyzeOpen(false)
                }}
              >
                はい
              </button>
            </div>
          </div>
        </div>
      )}
      {oneOpen && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
          <div className="w-full max-w-md rounded-md border border-[var(--line)] bg-[var(--panel)] p-4">
            <h2 className="text-base font-semibold">選択した音声だけを解析します</h2>
            <p className="mt-2 text-sm">
              {selectedNames.length > 1
                ? `${selectedNames.length} 件を解析し、いまの結果へ足します。`
                : `${selectedNames[0] ?? fileName} を解析し、いまの結果へ足します。`}
            </p>
            <div className="mt-4 flex gap-2">
              <button type="button" className="ghost-button flex-1" onClick={() => setOneOpen(false)}>いいえ</button>
              <button
                type="button"
                className="accent-button flex-1"
                onClick={() => {
                  sendRef.current({
                    type: "oto",
                    op: "analyzeFile",
                    names: selectedNames,
                  })
                  setOneOpen(false)
                }}
              >
                はい
              </button>
            </div>
          </div>
        </div>
      )}
      {overwriteWav && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
          <div className="w-full max-w-md rounded-md border border-[var(--line)] bg-[var(--panel)] p-4">
            <h2 className="text-base font-semibold">録音を上書きしますか？</h2>
            <p className="mt-2 text-sm">{overwriteWav} は既にあります。</p>
            <div className="mt-4 flex gap-2">
              <button type="button" className="ghost-button flex-1" onClick={() => setOverwriteWav(null)}>キャンセル</button>
              <button
                type="button"
                className="accent-button flex-1"
                onClick={() => {
                  const line = recIndexRef.current == null ? null : recListRef.current?.lines[recIndexRef.current]
                  setOverwriteWav(null)
                  if (line)
                    sendRecord(line.sing)
                }}
              >
                上書きする
              </button>
            </div>
          </div>
        </div>
      )}
      {settings && (
        <SettingsDialog
          tabs={settingsTabs(
            {
              theme,
              accent: accent ?? defaultAccent(resolved),
              scale,
              lockPanels,
              onTheme: chooseTheme,
              onAccent: chooseAccent,
              onScale: chooseScale,
              onLockPanels: chooseLock,
              onSystemAccent: () => {
                accentCustom.current = false
                setAccent(null)
                sendAppearance(theme, null)
              },
            },
            {
              inputs,
              outputs,
              inputId,
              outputId,
              onInput: (id) => {
                setInputId(id)
                sendRef.current({ type: "setDevices", inputId: id, outputId })
              },
              onOutput: (id) => {
                setOutputId(id)
                sendRef.current({ type: "setDevices", inputId, outputId: id })
              },
            },
          )}
          onClose={() => setSettings(false)}
        />
      )}
      {updatePrompt && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
          <div className="w-full max-w-md rounded-md border border-[var(--line)] bg-[var(--panel)] p-4">
            <h2 className="text-base font-semibold">{updatePrompt.title}</h2>
            <p className="mt-2 whitespace-pre-wrap text-sm">{updatePrompt.body}</p>
            <div className="mt-4 flex gap-2">
              <button type="button" className="ghost-button flex-1" onClick={() => setUpdatePrompt(null)}>閉じる</button>
              {updatePrompt.phase === "prompt" && (
                <button
                  type="button"
                  className="accent-button flex-1"
                  onClick={() => sendRef.current({ type: "update", op: "apply" })}
                >
                  更新する
                </button>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

