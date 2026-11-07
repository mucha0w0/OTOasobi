import { useCallback, useEffect, useRef, useState } from "react"
import { Dock } from "./Dock"
import { FileList } from "./FileList"
import { GraphBand, type BandHandle, type BandKind, type GraphModel } from "./GraphBand"
import { connectHost } from "./host"
import { contains, createDefault, hidePane, setRatio, showPane, type LayoutNode, type PaneBox } from "./layout"
import { createLiveBus, type LiveBus } from "./live"
import { MenuBar } from "./MenuBar"
import { requestsPreviewAfter, type DeviceInfo, type HostEvent, type PageCommand, type PreviewEvent, type SavedEvent } from "./protocol"
import { GuidePanel, MicPanel, RecPanel } from "./SidePanels"
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
  const modelRef = useRef<GraphModel>({ picture: null, range: rangeRef.current })
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
  const [note, setNote] = useState("A4")
  const [semitones, setSemitones] = useState(0)
  const [speed, setSpeed] = useState(1)
  const [guidePlaying, setGuidePlaying] = useState(false)
  const [folder, setFolder] = useState("")
  const [names, setNames] = useState<string[]>([])
  const [fileName, setFileName] = useState("")
  const [picture, setPicture] = useState<PreviewEvent | null>(null)
  const [notice, setNotice] = useState("")
  const [saved, setSaved] = useState<SavedEvent | null>(null)
  const [settings, setSettings] = useState(false)
  const [layout, setLayout] = useState<LayoutNode | null>(() => createDefault())
  const resolved = resolveTheme(theme, systemDark)
  const themeLabel = resolved === "dark" ? "ダーク" : "ライト"
  pictureRef.current = picture
  fileNameRef.current = fileName
  namesRef.current = names
  modelRef.current = { picture, range: rangeRef.current }

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
    }
    window.addEventListener("keydown", keys)
    return () => window.removeEventListener("keydown", keys)
  }, [])

  useEffect(() => {
    sendRef.current({ type: "watchMic", on: contains(layout, "rec") || contains(layout, "mic") })
  }, [layout])

  useEffect(() => {
    if (!fileName)
      return
    const frame = window.requestAnimationFrame(() => sendPreview(rangeRef.current))
    return () => window.cancelAnimationFrame(frame)
  }, [fileName])

  function redraw() {
    for (const band of bands.current)
      band.redraw()
  }

  function sendPreview(range: Range) {
    if (!fileNameRef.current)
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
    modelRef.current = { picture: pictureRef.current, range }
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
        const keep = event.names.some((name) => sameName(name, fileNameRef.current))
        setFolder(event.folder)
        setNames(event.names)
        if (!keep) {
          fileNameRef.current = ""
          setFileName("")
          setPicture(null)
          rangeRef.current = { t0: 0, t1: 0 }
        }
        break
      }
      case "opened":
        fileNameRef.current = event.name
        setFileName(event.name)
        setPicture(null)
        rangeRef.current = { t0: 0, t1: 0 }
        window.clearTimeout(previewTimer.current)
        break
      case "preview":
        setPicture(event)
        rangeRef.current = { t0: event.waveT0, t1: event.waveT1 }
        modelRef.current = { picture: event, range: rangeRef.current }
        break
      case "capture":
        bus.show(event)
        break
      case "guide":
        setGuidePlaying(event.playing)
        break
      case "saved":
        setSaved(event)
        setNotice(`${event.path} を書きました。入力 ${event.inputRate} Hz、ヘッダ ${event.headerRate} Hz / ${event.bits} bit / ${event.channels} ch。`)
        break
      case "failed":
        setNotice(event.message)
        break
    }
  }
  applyRef.current = applyHost

  function togglePane(id: string) {
    setLayout((current) => (contains(current, id) ? hidePane(current, id) : showPane(current, id, boxes.current)))
  }

  const appearance = `${resolved}-${accent}`

  return (
    <div className="flex h-full min-h-0 flex-col bg-[var(--bg)] text-[var(--text)]">
      <MenuBar
        layout={layout}
        onOpenFolder={() => sendRef.current({ type: "openFolder" })}
        onSettings={() => setSettings(true)}
        onClose={() => sendRef.current({ type: "close" })}
        onToggle={togglePane}
        onReset={() => setLayout(createDefault())}
      />
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
            onRatio={(path, ratio) => setLayout((current) => (current ? setRatio(current, path, ratio) : current))}
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
                  />
                )
              if (id === "rec")
                return <RecPanel bus={bus} saved={saved} send={(command) => sendRef.current(command)} />
              if (id === "guide")
                return (
                  <GuidePanel
                    note={note}
                    semitones={semitones}
                    speed={speed}
                    playing={guidePlaying}
                    setNote={setNote}
                    setSemitones={setSemitones}
                    setSpeed={setSpeed}
                    send={(command) => sendRef.current(command)}
                  />
                )
              if (id === "mic")
                return (
                  <MicPanel
                    bus={bus}
                    appearance={appearance}
                    monitor={monitor}
                    setMonitor={setMonitor}
                    send={(command) => sendRef.current(command)}
                  />
                )
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
          {!connected && <p className="text-sm text-[var(--muted)]">ホストに接続していません</p>}
        </main>
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
                  <input type="radio" name="theme" checked={theme === choice} onChange={() => setTheme(choice)} />
                  {choice === "light" ? "ライト" : choice === "dark" ? "ダーク" : "端末に合わせる"}
                </label>
              ))}
            </fieldset>
            <label className="mt-4 flex items-center gap-2 text-sm">
              アクセント
              <input type="color" value={accent} onChange={(event) => setAccent(event.target.value)} />
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
