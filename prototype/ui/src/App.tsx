import { useCallback, useEffect, useRef, useState } from "react"
import { BankPanel } from "./BankPanel"
import { Dock } from "./Dock"
import { EffectsPanel } from "./EffectsPanel"
import { FileList } from "./FileList"
import { FrqEditor } from "./FrqEditor"
import { FrqPanel } from "./FrqPanel"
import { GraphBand, type BandHandle, type BandKind, type GraphModel } from "./GraphBand"
import { connectHost } from "./host"
import { contains, createDefault, hidePane, setRatio, showPane, type LayoutNode, type PaneBox } from "./layout"
import { createLiveBus, type LiveBus } from "./live"
import { MenuBar } from "./MenuBar"
import { OtoTable } from "./OtoTable"
import { OtoIni, OtoSettings } from "./OtoPanels"
import { requestsPreviewAfter, type BankEvent, type DeviceInfo, type EffectsEvent, type FrqViewEvent, type HostEvent, type OtoMark, type OtoState, type PageCommand, type PreviewEvent, type RecListEvent, type SavedEvent } from "./protocol"
import { RecListPanel, RecScriptPanel } from "./RecPanels"
import { GuidePanel, MicPanel, RecPanel, type GuideFileState } from "./SidePanels"
import type { PhoneSpan } from "./spans"
import { buttonColors, resolveTheme, type ThemeChoice } from "./theme"
import type { Range } from "./viewport"

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
  const modelRef = useRef<GraphModel>({ picture: null, range: rangeRef.current, marks: [], spans: [], note: "" })
  const fileNameRef = useRef("")
  const namesRef = useRef<string[]>([])
  const previewTimer = useRef<number | undefined>(undefined)
  const busRef = useRef<LiveBus | null>(null)
  if (!busRef.current)
    busRef.current = createLiveBus()
  const bus = busRef.current

  const [connected, setConnected] = useState(false)
  const [theme, setTheme] = useState<ThemeChoice>("system")
  const [systemDark, setSystemDark] = useState(() => window.matchMedia("(prefers-color-scheme: dark)").matches)
  const [accent, setAccent] = useState("#0f766e")
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
  const [picture, setPicture] = useState<PreviewEvent | null>(null)
  const [notice, setNotice] = useState("")
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
  const themeLabel = resolved === "dark" ? "ダーク" : "ライト"
  pictureRef.current = picture
  fileNameRef.current = fileName
  namesRef.current = names
  recListRef.current = recList
  recIndexRef.current = recIndex
  advanceRef.current = advance
  modelRef.current = { picture, range: rangeRef.current, marks: marksRef.current, spans: spansRef.current, note: noteRef.current }

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
    const colors = buttonColors(accent)
    document.documentElement.style.setProperty("--accent", colors.fill)
    document.documentElement.style.setProperty("--on-accent", colors.ink)
    sendRef.current({ type: "caption", theme: resolved })
  }, [resolved, accent])

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
    }
    window.addEventListener("keydown", keys)
    return () => window.removeEventListener("keydown", keys)
  }, [oto])

  useEffect(() => {
    sendRef.current({ type: "watchMic", on: contains(layout, "rec") || contains(layout, "mic") })
  }, [layout])

  useEffect(() => {
    if (!fileName || missingRef.current)
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

  function redraw() {
    for (const band of bands.current)
      band.redraw()
  }

  function sendPreview(range: Range) {
    if (!fileNameRef.current || missingRef.current)
      return
    const spec = [...bands.current].find((band) => band.kind === "spec")
    const measured = spec?.pixelSize() ?? { columns: 800, rows: 320 }
    sendRef.current({ type: "preview", t0: range.t0, t1: range.t1, columns: measured.columns, rows: measured.rows })
  }

  function schedulePreview() {
    window.clearTimeout(previewTimer.current)
    previewTimer.current = window.setTimeout(() => sendPreview(rangeRef.current), 180)
  }

  function onRange(range: Range, commit: boolean) {
    rangeRef.current = range
    modelRef.current = { picture: pictureRef.current, range, marks: marksRef.current, spans: spansRef.current, note: noteRef.current }
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

  function stepFile(direction: number) {
    const list = namesRef.current
    if (list.length === 0)
      return
    const index = list.findIndex((name) => sameName(name, fileNameRef.current))
    const next = Math.min(list.length - 1, Math.max(0, (index < 0 ? 0 : index) + direction))
    const name = list[next]
    if (name && !sameName(name, fileNameRef.current))
      sendRef.current({ type: "selectFile", name })
  }

  function applyHost(event: HostEvent) {
    switch (event.type) {
      case "devices":
        setInputs(event.inputs)
        setOutputs(event.outputs)
        break
      case "files": {
        const keep = missingRef.current || event.names.some((name) => sameName(name, fileNameRef.current))
        setFolder(event.folder)
        setNames(event.names)
        if (!keep) {
          fileNameRef.current = ""
          setFileName("")
          setPicture(null)
          rangeRef.current = { t0: 0, t1: 0 }
          noteRef.current = ""
        }
        break
      }
      case "opened":
        fileNameRef.current = event.name
        missingRef.current = event.missing === true
        noteRef.current = event.missing ? "このファイルは存在しません" : ""
        setFileName(event.name)
        setPicture(null)
        rangeRef.current = { t0: 0, t1: 0 }
        modelRef.current = { picture: null, range: rangeRef.current, marks: marksRef.current, spans: spansRef.current, note: noteRef.current }
        window.clearTimeout(previewTimer.current)
        redraw()
        syncRecIndex(event.name)
        break
      case "preview":
        missingRef.current = false
        noteRef.current = ""
        setPicture(event)
        rangeRef.current = { t0: event.waveT0, t1: event.waveT1 }
        modelRef.current = { picture: event, range: rangeRef.current, marks: marksRef.current, spans: spansRef.current, note: "" }
        break
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
      case "templates":
        setTemplates(event.names)
        break
      case "appearance":
        setTheme(event.theme)
        if (event.accent) {
          accentCustom.current = true
          setAccent(event.accent)
        } else {
          accentCustom.current = false
        }
        break
      case "update":
        setUpdatePrompt({ phase: event.phase, title: event.title, body: event.body })
        break
      case "saved":
        setSaved(event)
        setNotice(`${event.path} を書きました。入力 ${event.inputRate} Hz、ヘッダ ${event.headerRate} Hz / ${event.bits} bit / ${event.channels} ch。`)
        advanceAfterSave()
        break
      case "failed":
      case "notice":
        setNotice(event.message)
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
        modelRef.current = { ...modelRef.current, marks: event.marks, spans: event.spans }
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

  function chooseTheme(choice: ThemeChoice) {
    setTheme(choice)
    sendRef.current({ type: "appearance", theme: choice, accent: accentCustom.current ? accent : null })
  }

  function chooseAccent(value: string) {
    accentCustom.current = true
    setAccent(value)
    sendRef.current({ type: "appearance", theme, accent: value })
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
      sendRef.current({ type: "selectFile", name: wav })
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
      setNotice(line.error)
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
  const appearance = `${resolved}-${accent}`

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
        <div className="border-b border-[var(--line)] bg-[var(--panel)] px-4 py-2 text-sm">{status}</div>
      )}
      {notice && (
        <div className="flex items-center justify-between border-b border-[var(--line)] bg-[var(--panel)] px-4 py-2 text-sm">
          <span>{notice}</span>
          <button type="button" className="ghost-button" onClick={() => setNotice("")}>閉じる</button>
        </div>
      )}
      {folder ? (
        <div className="min-h-0 flex-1">
          <Dock
            layout={layout}
            boxes={boxes}
            onChange={(next, persist) => {
              if (persist)
                commitLayout(next)
              else
                setLayout(next)
            }}
            onRatio={(path, ratio) => commitLayout(layout ? setRatio(layout, path, ratio) : layout, true)}
            onResizing={(holding) => {
              for (const band of bands.current)
                band.holdLayout(holding)
              if (!holding)
                sendPreview(rangeRef.current)
            }}
            render={(id) => {
              if (id === "file")
                return (
                  <FileList
                    folder={folder}
                    names={names}
                    selected={fileName}
                    onSelect={(name) => sendRef.current({ type: "selectFile", name })}
                  />
                )
              if (id in graphKinds)
                return (
                  <GraphBand
                    kind={graphKinds[id]!}
                    picture={picture}
                    model={modelRef}
                    appearance={appearance}
                    register={register}
                    onRange={onRange}
                    onFileStep={stepFile}
                    onTune={id === "otoBand" ? (index, point, time) => sendRef.current({ type: "oto", op: "tune", file: fileNameRef.current, index, point, time }) : undefined}
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
                return <FrqEditor view={frqView} send={(command) => sendRef.current(command)} />
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
          />
        </div>
      ) : (
        <main className="flex flex-1 flex-col items-center justify-center gap-2 px-6 text-center">
          <h1 className="text-2xl font-semibold">オトアソビ</h1>
          <p className="text-sm text-[var(--muted)]">OTOasobi</p>
          <p className="max-w-md text-sm leading-6 text-[var(--muted)]">
            フォルダを開くと編集画面に入ります。保存データがあるときは、その区間を読み込みます。無いときは、解析してから区間を直せます。
          </p>
          <button type="button" className="accent-button mt-2 min-w-52" onClick={() => sendRef.current({ type: "openFolder" })}>
            既存のフォルダを選ぶ
          </button>
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
      {saveOpen && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
          <form
            className="w-full max-w-md rounded-lg border border-[var(--line)] bg-[var(--panel)] p-4"
            onSubmit={(event) => {
              event.preventDefault()
              sendRef.current({ type: "saveSettings", name: saveName, replace: false })
              setSaveOpen(false)
            }}
          >
            <h2 className="text-base font-semibold">設定を保存</h2>
            <p className="mt-2 text-sm leading-6 text-[var(--muted)]">
              いまの計算方法、スライダー、プリセットを、このフォルダに保存します。
            </p>
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
          <div className="w-full max-w-md rounded-lg border border-[var(--line)] bg-[var(--panel)] p-4">
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
          <div className="w-full max-w-md rounded-lg border border-[var(--line)] bg-[var(--panel)] p-4">
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
            ) : (
              <p className="mt-3 text-sm text-[var(--muted)]">このフォルダには、まだ設定ファイルがありません。「設定を保存」で作れます。</p>
            )}
            <button type="button" className="ghost-button mt-4 w-full" onClick={() => setLoadOpen(false)}>閉じる</button>
          </div>
        </div>
      )}
      {reanalyzeOpen && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
          <div className="w-full max-w-md rounded-lg border border-[var(--line)] bg-[var(--panel)] p-4">
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
          <div className="w-full max-w-md rounded-lg border border-[var(--line)] bg-[var(--panel)] p-4">
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
          <div className="w-full max-w-md rounded-lg border border-[var(--line)] bg-[var(--panel)] p-4">
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
        <div className="fixed inset-0 z-30 flex items-center justify-center bg-black/40 p-6">
          <div className="w-full max-w-lg rounded-lg border border-[var(--line)] bg-[var(--panel)] p-6 text-[var(--text)]">
            <h2 className="text-lg font-semibold">環境設定</h2>
            <p className="mt-2 text-sm leading-6 text-[var(--muted)]">
              明暗は{themeLabel}です。タイトルバーへも同じ明暗を送っています。
            </p>
            <fieldset className="mt-4 flex gap-3 text-sm">
              {(["light", "dark", "system"] as const).map((choice) => (
                <label key={choice} className="flex items-center gap-1">
                  <input type="radio" name="theme" checked={theme === choice} onChange={() => chooseTheme(choice)} />
                  {choice === "light" ? "ライト" : choice === "dark" ? "ダーク" : "端末に合わせる"}
                </label>
              ))}
            </fieldset>
            <label className="mt-4 flex items-center gap-2 text-sm">
              アクセント
              <input type="color" value={accent} onChange={(event) => chooseAccent(event.target.value)} />
              <button type="button" className="ghost-button" onClick={() => {
                accentCustom.current = false
                sendRef.current({ type: "appearance", theme, accent: null })
              }}>端末に合わせる</button>
            </label>
            <label className="mt-4 grid gap-1 text-sm">
              入力
              <select className="field" value={inputId} onChange={(event) => {
                setInputId(event.target.value)
                sendRef.current({ type: "setDevices", inputId: event.target.value, outputId })
              }}>
                <option value="">既定</option>
                {inputs.map((device) => <option key={device.id} value={device.id}>{device.label}</option>)}
              </select>
            </label>
            <label className="mt-3 grid gap-1 text-sm">
              出力
              <select className="field" value={outputId} onChange={(event) => {
                setOutputId(event.target.value)
                sendRef.current({ type: "setDevices", inputId, outputId: event.target.value })
              }}>
                <option value="">既定</option>
                {outputs.map((device) => <option key={device.id} value={device.id}>{device.label}</option>)}
              </select>
            </label>
            <button type="button" className="accent-button mt-5 w-full" onClick={() => setSettings(false)}>閉じる</button>
          </div>
        </div>
      )}
      {updatePrompt && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
          <div className="w-full max-w-md rounded-lg border border-[var(--line)] bg-[var(--panel)] p-4">
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
      <style>{`
        .accent-button {
          background: var(--accent);
          color: var(--on-accent);
          border-radius: 6px;
          min-height: 32px;
          padding: 6px 12px;
        }
        .ghost-button, .field {
          background: var(--panel);
          color: var(--text);
          border: 1px solid var(--line);
          border-radius: 6px;
          min-height: 32px;
          padding: 4px 8px;
        }
      `}</style>
    </div>
  )
}

function sameName(left: string, right: string) {
  return left.localeCompare(right, undefined, { sensitivity: "accent" }) === 0
}
