import { useCallback, useEffect, useRef, useState } from "react"
import { BankPanel } from "./BankPanel"
import { Dock } from "./Dock"
import { EffectsPanel } from "./EffectsPanel"
import { FileList, type FileListHandle } from "./FileList"
import { nextFileIndex } from "./fileStep"
import { FrqEditor } from "./FrqEditor"
import { FrqPanel } from "./FrqPanel"
import { GraphBand, type BandHandle, type BandKind, type GraphModel, type LiveTune } from "./GraphBand"
import { connectHost } from "./host"
import { contains, createDefault, hidePane, idsOf, showPane, type LayoutNode, type PaneBox } from "./layout"
import { createLiveBus, type LiveBus } from "./live"
import { MenuBar } from "./MenuBar"
import { OtoTable } from "./OtoTable"
import { OtoIni, OtoSettings } from "./OtoPanels"
import { previewColumnLimit, previewRowLimit, requestsPreviewAfter, uiScale, type BankEvent, type DeviceInfo, type EffectsEvent, type FrqViewEvent, type HostEvent, type OtoMark, type OtoState, type PageCommand, type PreviewEvent, type RecListEvent, type SavedEvent } from "./protocol"
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

function mergePreview(current: PreviewEvent | null, event: PreviewEvent): PreviewEvent {
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
      loudness: event.loudness.length > 0 ? event.loudness : current.loudness,
      sampleRate: event.sampleRate || current.sampleRate,
    }
  if (hasWave && !hasSpec)
    return { ...event, spec: "", specColumns: 0, specRows: 0, png: "" }
  return event
}

const graphKinds: Record<string, BandKind> = {
  wave: "wave",
  spec: "spec",
  loud: "loud",
  otoBand: "oto",
}

export function App() {
  const sendRef = useRef<(command: PageCommand) => void>(() => undefined)
  const applyRef = useRef<(event: HostEvent) => void>(() => undefined)
  const bands = useRef(new Set<BandHandle>())
  const boxes = useRef<Record<string, PaneBox>>({})
  const pictureRef = useRef<PreviewEvent | null>(null)
  const rangeRef = useRef<Range>({ t0: 0, t1: 0 })
  const marksRef = useRef<OtoMark[]>([])
  const spansRef = useRef<PhoneSpan[]>([])
  const noteRef = useRef("")
  const missingRef = useRef(false)
  const focusRef = useRef(-1)
  const liveRef = useRef<LiveTune | null>(null)
  const draftRef = useRef<PhoneSpan[] | null>(null)
  const modelRef = useRef<GraphModel>({ picture: null, range: rangeRef.current, marks: [], spans: [], note: "", focus: -1, live: null, draft: null })
  const fileNameRef = useRef("")
  const namesRef = useRef<string[]>([])
  const previewTimer = useRef<number | undefined>(undefined)
  const fileStepTimer = useRef<number | undefined>(undefined)
  const stepTarget = useRef<number | null>(null)
  const busRef = useRef<LiveBus | null>(null)
  if (!busRef.current)
    busRef.current = createLiveBus()
  const bus = busRef.current

  const [connected, setConnected] = useState(false)
  const [theme, setTheme] = useState<ThemeChoice>("system")
  const [scale, setScale] = useState(() => uiScale(Number.parseFloat(document.documentElement.style.zoom)))
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
  const [bpm, setBpm] = useState(120)
  const [guideWhile, setGuideWhile] = useState(false)
  const [advance, setAdvance] = useState(false)
  const [guidePlaying, setGuidePlaying] = useState(false)
  const [folder, setFolder] = useState("")
  const [names, setNames] = useState<string[]>([])
  const [fileName, setFileName] = useState("")
  const fileListRef = useRef<FileListHandle>(null)
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
  const [updatePrompt, setUpdatePrompt] = useState<{ phase: string; title: string; body: string } | null>(null)
  const layoutTouched = useRef(false)
  const acceptLayout = useRef(false)
  const keepRatios = useRef(false)
  const accentCustom = useRef(false)
  const recTouched = useRef(false)
  const recListRef = useRef<RecListEvent | null>(null)
  const recIndexRef = useRef<number | null>(null)
  const advanceRef = useRef(false)
  const recordNameRef = useRef<string | null>(null)
  const resolved = resolveTheme(theme, systemDark)
  pictureRef.current = picture
  fileNameRef.current = fileName
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
    sendRef.current = connection.send
    setConnected(window.chrome?.webview != null)
    connection.send({ type: "hello" })
    return () => {
      window.clearTimeout(previewTimer.current)
      connection.close()
    }
  }, [])

  useEffect(() => {
    const media = window.matchMedia("(prefers-color-scheme: dark)")
    const apply = () => setSystemDark(media.matches)
    media.addEventListener("change", apply)
    return () => media.removeEventListener("change", apply)
  }, [])

  useEffect(() => {
    document.documentElement.dataset.theme = resolved
    const colors = buttonColors(correctAccent(accent ?? defaultAccent(resolved), resolved))
    document.documentElement.style.setProperty("--accent", colors.fill)
    document.documentElement.style.setProperty("--on-accent", colors.ink)
    sendRef.current({ type: "caption", theme: resolved })
  }, [resolved, accent])

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
        focusRef.current = -1
        liveRef.current = null
        draftRef.current = null
        modelRef.current = viewModel()
        redraw()
      }
    }
    window.addEventListener("keydown", keys)
    return () => window.removeEventListener("keydown", keys)
  }, [oto])

  useEffect(() => {
    sendRef.current({ type: "watchMic", on: contains(layout, "rec") || contains(layout, "mic") })
  }, [layout])

  useEffect(() => {
    if (!fileName || missingRef.current || stepTarget.current != null)
      return
    const frame = window.requestAnimationFrame(() => sendPreview(rangeRef.current))
    return () => window.cancelAnimationFrame(frame)
  }, [fileName])

  useEffect(() => {
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
        bpm,
        guideWhile,
        advance,
      })
    }, 300)
    return () => window.clearTimeout(timer)
  }, [advance, bpm, countIn, guideWhile, note, semitones, speed, volume])

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
    if (focusRef.current === index)
      return
    focusRef.current = index
    modelRef.current = viewModel()
    redraw()
  }

  function sendPreview(range: Range) {
    if (!fileNameRef.current || missingRef.current)
      return
    const spec = [...bands.current].find((band) => band.kind === "spec")
    const measured = spec?.pixelSize() ?? { columns: 800, rows: 256 }
    sendRef.current({
      type: "preview",
      t0: range.t0,
      t1: range.t1,
      columns: Math.min(previewColumnLimit, Math.max(32, measured.columns)),
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

  function chooseFile(name: string) {
    window.clearTimeout(fileStepTimer.current)
    stepTarget.current = null
    fileListRef.current?.highlight(name)
    sendRef.current({ type: "selectFile", name })
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
    fileListRef.current?.highlight(name)
    window.clearTimeout(fileStepTimer.current)
    fileStepTimer.current = window.setTimeout(() => {
      const landed = stepTarget.current
      const chosen = landed == null ? "" : namesRef.current[landed] ?? ""
      if (chosen && !sameName(chosen, fileNameRef.current))
        sendRef.current({ type: "selectFile", name: chosen })
      else {
        stepTarget.current = null
        fileListRef.current?.release()
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
        setFolder(event.folder)
        setNames(event.names)
        window.clearTimeout(fileStepTimer.current)
        stepTarget.current = null
        fileListRef.current?.release()
        if (!keep) {
          fileNameRef.current = ""
          setFileName("")
          setPicture(null)
          rangeRef.current = { t0: 0, t1: 0 }
          noteRef.current = ""
          focusRef.current = -1
          liveRef.current = null
          draftRef.current = null
        }
        break
      }
      case "opened": {
        fileNameRef.current = event.name
        missingRef.current = event.missing === true
        noteRef.current = event.missing ? "このファイルは存在しません" : ""
        setFileName(event.name)
        const pending = stepTarget.current == null ? null : namesRef.current[stepTarget.current]
        if (pending == null || sameName(pending, event.name)) {
          stepTarget.current = null
          fileListRef.current?.release()
        }
        if (event.missing) {
          setPicture(null)
          pictureRef.current = null
          rangeRef.current = { t0: 0, t1: 0 }
        }
        focusRef.current = -1
        liveRef.current = null
        draftRef.current = null
        modelRef.current = viewModel()
        window.clearTimeout(previewTimer.current)
        redraw()
        syncRecIndex(event.name)
        break
      }
      case "preview": {
        if (stepTarget.current != null)
          break
        missingRef.current = false
        noteRef.current = ""
        const next = mergePreview(pictureRef.current, event)
        setPicture(next)
        pictureRef.current = next
        if (event.lo.length > 0)
          rangeRef.current = { t0: event.waveT0, t1: event.waveT1 }
        modelRef.current = viewModel()
        break
      }
      case "capture":
        bus.show(event)
        break
      case "guide":
        setGuidePlaying(event.playing)
        break
      case "guideFile":
        setGuideFile({ manual: event.manual, path: event.path, label: event.label, recent: event.recent })
        break
      case "take":
        setTakePlay({ playing: event.playing, position: event.position, duration: event.duration })
        break
      case "takeWave":
        setTakeWave(event)
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
        setTheme(event.theme)
        setScale(event.scale)
        applyPageScale(event.scale)
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
      case "oto":
        marksRef.current = event.marks
        spansRef.current = event.spans
        if (focusRef.current >= 0 && !event.marks.some((mark) => mark.index === focusRef.current))
          focusRef.current = -1
        modelRef.current = viewModel()
        setOto(event)
        for (const band of bands.current)
          band.redraw()
        break
      case "frq":
        setFrqCount({ wavs: event.wavs, frqs: event.frqs, busy: event.busy })
        break
      case "frqView":
        setFrqView(event)
        break
      case "recList":
        setRecList(event)
        setRecIndex((index) => {
          const previous = recListRef.current?.lines[index ?? -1]
          if (!previous)
            return index != null && index < event.lines.length ? index : null
          const next = event.lines.findIndex((line) => sameName(line.wav, previous.wav))
          return next >= 0 ? next : null
        })
        break
      case "bank":
        setBank(event)
        break
      case "effects":
        setEffects(event)
        break
      case "recSettings":
        if (recTouched.current)
          break
        setNote(event.note)
        setSemitones(event.semitones)
        setSpeed(event.speed)
        setVolume(event.volume)
        setCountIn(event.countIn)
        setBpm(event.bpm)
        setGuideWhile(event.guideWhile)
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

  function sendAppearance(nextTheme: ThemeChoice, nextAccent: string | null, nextScale = scale) {
    sendRef.current({ type: "appearance", theme: nextTheme, accent: nextAccent, scale: nextScale })
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
      chooseFile(wav)
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
      bpm,
      guideWhile,
      volume,
      note,
      semitones,
      speed,
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

  const recLine = recIndex == null ? null : recList?.lines[recIndex] ?? null
  useEffect(() => {
    if (recLine?.recorded)
      sendRef.current({ type: "take", op: "wave", name: recLine.wav })
    else
      setTakeWave(null)
  }, [recLine?.recorded, recLine?.wav])
  const recNext = recLine && recIndex != null ? recList?.lines[recIndex + 1]?.sing ?? "" : ""
  const recOutside = !recLine && (recList?.outside.some((name) => sameName(name, fileName)) ?? false) ? fileName : ""
  const recStatus = recLine?.error ?? (recLine ? (recLine.recorded ? "録音済み" : "未録音") : recOutside ? "リスト外" : "")
  const appearance = `${resolved}-${buttonColors(correctAccent(accent ?? defaultAccent(resolved), resolved)).fill}-${scale}`

  return (
    <div className="flex h-full min-h-0 flex-col bg-[var(--bg)] text-[var(--text)]">
      <MenuBar
        layout={layout}
        canEdit={folder.length > 0}
        canWrite={Boolean(oto?.analyzed && !oto.busy)}
        canAnalyze={folder.length > 0 && !oto?.busy}
        canAnalyzeOne={folder.length > 0 && fileName.length > 0 && !missingRef.current && !oto?.busy}
        onOpenFolder={() => sendRef.current({ type: "openFolder" })}
        onSaveSettings={() => {
          setSaveName("")
          setSaveOpen(true)
        }}
        onLoadSettings={() => setLoadOpen(true)}
        onWrite={() => sendRef.current({ type: "oto", op: "write" })}
        onReanalyze={() => setReanalyzeOpen(true)}
        onReanalyzeOne={() => setOneOpen(true)}
        onSettings={() => setSettings(true)}
        onClose={() => sendRef.current({ type: "close" })}
        onToggle={togglePane}
        onReset={() => applyTemplate("初期配置")}
        onRecording={() => applyTemplate("録音")}
        templates={templates}
        onTemplate={applyTemplate}
        onSaveTemplate={saveTemplate}
        onCheckUpdate={() => sendRef.current({ type: "update", op: "check" })}
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
        <div className="min-h-0 flex-1">
          <PaneSlots
            ids={layout ? idsOf(layout) : []}
            render={(id) => {
              if (id === "file")
                return (
                  <FileList
                    ref={fileListRef}
                    folder={folder}
                    names={names}
                    selected={fileName}
                    onSelect={chooseFile}
                  />
                )
              if (id in graphKinds)
                return (
                  <GraphBand
                    kind={graphKinds[id]!}
                    picture={picture}
                    model={modelRef}
                    appearance={appearance}
                    pointerBus={pointerBus}
                    register={register}
                    onRange={onRange}
                    onFileStep={stepFile}
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
                    bpm={bpm}
                    guideWhile={guideWhile}
                    playing={guidePlaying}
                    setNote={touchRec(setNote)}
                    setSemitones={touchRec(setSemitones)}
                    setSpeed={touchRec(setSpeed)}
                    setVolume={touchRec(setVolume)}
                    setCountIn={touchRec(setCountIn)}
                    setBpm={touchRec(setBpm)}
                    setGuideWhile={touchRec(setGuideWhile)}
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
                  />
                )
              if (id === "recScript")
                return <RecScriptPanel line={recLine} next={recNext} outside={recOutside} />
              if (id === "frq")
                return (
                  <FrqPanel
                    wavs={frqCount.wavs}
                    frqs={frqCount.frqs}
                    busy={Boolean(oto?.busy) || frqCount.busy}
                    hasFile={fileName.length > 0 && !missingRef.current}
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
                return <EffectsPanel effects={effects} busy={Boolean(oto?.busy)} send={(command) => sendRef.current(command)} />
              if (id === "bank")
                return <BankPanel bank={bank} send={(command) => sendRef.current(command)} />
              if (id === "oto")
                return <OtoSettings oto={oto} send={(command) => sendRef.current(command)} />
              if (id === "otoIni")
                return <OtoIni oto={oto} send={(command) => sendRef.current(command)} />
              return null
            }}
          >
            <Dock
              layout={layout}
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
            <p className="mt-2 text-sm">{fileName} を解析し、いまの結果へ足します。</p>
            <div className="mt-4 flex gap-2">
              <button type="button" className="ghost-button flex-1" onClick={() => setOneOpen(false)}>いいえ</button>
              <button
                type="button"
                className="accent-button flex-1"
                onClick={() => {
                  sendRef.current({ type: "oto", op: "analyzeFile" })
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
              onTheme: chooseTheme,
              onAccent: chooseAccent,
              onScale: chooseScale,
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

function sameName(left: string, right: string) {
  return left.localeCompare(right, undefined, { sensitivity: "accent" }) === 0
}
