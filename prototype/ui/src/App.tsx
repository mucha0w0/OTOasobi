import { useEffect, useRef, useState } from "react"
import { FileList } from "./FileList"
import { connectHost } from "./host"
import { LiveMonitor, type LiveHandle } from "./LiveMonitor"
import {
  requestsPreview,
  requestsPreviewAfter,
  type HostEvent,
  type DeviceInfo,
  type PageCommand,
  type PreviewEvent,
  type PreviewGesture,
  type SavedEvent,
} from "./protocol"
import { buttonColors, resolveTheme, type ThemeChoice } from "./theme"
import { WaveView, type WaveHandle } from "./WaveView"

const notes = ["C3", "D3", "E3", "F3", "G3", "A3", "B3", "C4", "D4", "E4", "F4", "G4", "A4", "B4", "C5", "D5", "E5", "F5", "G5", "A5", "B5", "C6"]

export function App() {
  const sendRef = useRef<(command: PageCommand) => void>(() => undefined)
  const waveRef = useRef<WaveHandle | null>(null)
  const liveRef = useRef<LiveHandle | null>(null)
  const previewTimer = useRef<number | undefined>(undefined)
  const pictureRef = useRef<PreviewEvent | null>(null)
  const pointerText = useRef<HTMLSpanElement>(null)
  const boundaryText = useRef<HTMLSpanElement>(null)
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
  const [boundary, setBoundary] = useState(0)
  const [notice, setNotice] = useState("")
  const [saved, setSaved] = useState<SavedEvent | null>(null)
  const [dialog, setDialog] = useState<string | null>(null)
  const [fraction, setFraction] = useState(0.68)
  const fractionRef = useRef(0.68)
  const frameRef = useRef<HTMLDivElement>(null)
  const sectionRef = useRef<HTMLElement>(null)
  const fileNameRef = useRef("")
  const applyRef = useRef<(event: HostEvent) => void>(() => undefined)
  pictureRef.current = picture
  fileNameRef.current = fileName
  const resolved = resolveTheme(theme, systemDark)
  const themeLabel = resolved === "dark" ? "ダーク" : "ライト"

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

  applyRef.current = applyHost

  function sameName(left: string, right: string) {
    return left.localeCompare(right, undefined, { sensitivity: "accent" }) === 0
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
        }
        break
      }
      case "opened":
        fileNameRef.current = event.name
        setFileName(event.name)
        setPicture(null)
        sendPreview(0, 0)
        break
      case "preview":
        setPicture(event)
        setBoundary((current) => {
          const next = Math.min(Math.max(current, event.waveT0), Math.max(event.waveT0, event.duration))
          writeBoundary(next)
          return next
        })
        break
      case "capture":
        liveRef.current?.show(event)
        break
      case "guide":
        setGuidePlaying(event.playing)
        break
      case "saved":
        setSaved(event)
        setDialog("録音を保存しました")
        break
      case "failed":
        setNotice(event.message)
        break
    }
  }

  function sendPreview(t0: number, t1: number) {
    const size = waveRef.current?.pixelSize() ?? { columns: 800, rows: 320 }
    sendRef.current({ type: "preview", t0, t1, columns: size.columns, rows: size.rows })
  }

  function onPreview(gesture: PreviewGesture, t0: number, t1: number) {
    if (!requestsPreview(gesture))
      return
    window.clearTimeout(previewTimer.current)
    previewTimer.current = window.setTimeout(() => sendPreview(t0, t1), 80)
  }

  function writeBoundary(time: number) {
    if (boundaryText.current)
      boundaryText.current.textContent = `境界 ${time.toFixed(3)} 秒`
  }

  function onBoundary(time: number, commit: boolean) {
    writeBoundary(time)
    if (commit)
      setBoundary(time)
  }

  function onPointerTime(time: number | null) {
    if (pointerText.current)
      pointerText.current.textContent = time == null ? "ポインタ" : `ポインタ ${time.toFixed(3)} 秒`
  }

  return (
    <div className="flex h-full min-h-0 flex-col bg-[var(--bg)] text-[var(--text)]">
      <header className="flex flex-wrap items-center gap-3 border-b border-[var(--line)] px-4 py-3">
        <div className="text-sm font-semibold">移行の試作</div>
        <fieldset className="flex gap-2 text-sm">
          {(["light", "dark", "system"] as const).map((choice) => (
            <label key={choice} className="flex items-center gap-1">
              <input
                type="radio"
                name="theme"
                checked={theme === choice}
                onChange={() => setTheme(choice)}
              />
              {choice === "light" ? "ライト" : choice === "dark" ? "ダーク" : "端末に合わせる"}
            </label>
          ))}
        </fieldset>
        <label className="flex items-center gap-2 text-sm">
          アクセント
          <input type="color" value={accent} onChange={(event) => setAccent(event.target.value)} />
        </label>
        <button type="button" className="accent-button" onClick={() => sendRef.current({ type: "openWav" })}>
          WAV を開く
        </button>
        <span className="text-sm text-[var(--muted)]">{fileName || "ファイルは未選択"}</span>
        <button type="button" className="ghost-button" onClick={() => setDialog("テーマの確認")}>
          ダイアログを見る
        </button>
        {!connected && <span className="text-sm text-[var(--muted)]">ホストに接続していません</span>}
      </header>
      {notice && (
        <div className="flex items-center justify-between border-b border-[var(--line)] bg-[var(--panel)] px-4 py-2 text-sm">
          <span>{notice}</span>
          <button type="button" className="ghost-button" onClick={() => setNotice("")}>閉じる</button>
        </div>
      )}
      <div className="flex min-h-0 flex-1">
        <FileList
          folder={folder}
          names={names}
          selected={fileName}
          onOpenFolder={() => sendRef.current({ type: "openFolder" })}
          onSelect={(name) => sendRef.current({ type: "selectFile", name })}
        />
        <div ref={frameRef} className="flex min-w-0 flex-1">
          <section ref={sectionRef} className="min-w-0" style={{ width: `${fraction * 100}%` }}>
            <div className="flex items-center justify-between px-3 py-2 text-xs text-[var(--muted)]">
              <span ref={boundaryText}>境界 {boundary.toFixed(3)} 秒</span>
              <span ref={pointerText}>ポインタ</span>
            </div>
            <div className="h-[calc(100%-2rem)] bg-[var(--canvas)]">
              <WaveView
                picture={picture}
                boundary={boundary}
                appearance={`${resolved}-${accent}`}
                onBoundary={onBoundary}
                onPreview={onPreview}
                onPointerTime={onPointerTime}
                handle={waveRef}
              />
            </div>
          </section>
          <div
            className="w-1.5 cursor-col-resize bg-[var(--line)]"
            onPointerDown={(event) => {
              const frame = frameRef.current
              if (!frame)
                return
              const bounds = frame.getBoundingClientRect()
              waveRef.current?.holdLayout(true)
              const move = (ev: PointerEvent) => {
                const min = 160 / bounds.width
                const aside = 280 / bounds.width
                const next = Math.min(1 - aside, Math.max(min, (ev.clientX - bounds.left) / bounds.width))
                fractionRef.current = next
                if (sectionRef.current)
                  sectionRef.current.style.width = `${next * 100}%`
              }
              const up = () => {
                window.removeEventListener("pointermove", move)
                window.removeEventListener("pointerup", up)
                waveRef.current?.holdLayout(false)
                setFraction(fractionRef.current)
                const current = pictureRef.current
                if (current && requestsPreviewAfter("resize"))
                  sendPreview(current.waveT0, current.waveT1)
              }
              window.addEventListener("pointermove", move)
              window.addEventListener("pointerup", up)
              event.currentTarget.setPointerCapture(event.pointerId)
            }}
          />
          <aside className="flex min-w-[280px] flex-1 flex-col gap-4 overflow-auto border-l border-[var(--line)] bg-[var(--panel)] p-4">
            <label className="grid gap-1 text-sm">
              入力
              <select
                className="field"
                value={inputId}
                onChange={(event) => {
                  setInputId(event.target.value)
                  sendRef.current({ type: "setDevices", inputId: event.target.value, outputId })
                }}
              >
                <option value="">既定</option>
                {inputs.map((device) => (
                  <option key={device.id} value={device.id}>{device.label}</option>
                ))}
              </select>
            </label>
            <label className="grid gap-1 text-sm">
              出力
              <select
                className="field"
                value={outputId}
                onChange={(event) => {
                  setOutputId(event.target.value)
                  sendRef.current({ type: "setDevices", inputId, outputId: event.target.value })
                }}
              >
                <option value="">既定</option>
                {outputs.map((device) => (
                  <option key={device.id} value={device.id}>{device.label}</option>
                ))}
              </select>
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={monitor}
                onChange={(event) => {
                  setMonitor(event.target.checked)
                  sendRef.current({ type: "monitor", on: event.target.checked })
                }}
              />
              入力を聴く
            </label>
            <LiveMonitor sink={liveRef} send={(command) => sendRef.current(command)} appearance={`${resolved}-${accent}`}>
              <label className="grid gap-1 text-sm">
                ガイドの音
                <select className="field" value={note} onChange={(event) => setNote(event.target.value)}>
                  {notes.map((item) => (
                    <option key={item}>{item}</option>
                  ))}
                </select>
              </label>
              <label className="grid gap-1 text-sm">
                音程 {semitones} 半音
                <input type="range" min={-12} max={12} step={1} value={semitones} onChange={(event) => setSemitones(Number(event.target.value))} />
              </label>
              <label className="grid gap-1 text-sm">
                速さ {Math.round(speed * 100)}%
                <input type="range" min={0.5} max={2} step={0.05} value={speed} onChange={(event) => setSpeed(Number(event.target.value))} />
              </label>
              <button
                type="button"
                className="accent-button"
                onClick={() => sendRef.current(guidePlaying ? { type: "guideStop" } : { type: "guide", note, semitones, speed })}
              >
                {guidePlaying ? "ガイドを停止" : "ガイドを聴く"}
              </button>
            </LiveMonitor>
            {saved && (
              <p className="text-sm leading-6">
                入力 {saved.inputRate} Hz、ヘッダ {saved.headerRate} Hz / {saved.bits} bit / {saved.channels} ch、
                ファイル {saved.seconds.toFixed(2)} 秒、操作 {saved.elapsed.toFixed(2)} 秒
              </p>
            )}
          </aside>
        </div>
      </div>
      {dialog && (
        <div className="fixed inset-0 z-10 flex items-center justify-center bg-black/40 p-6">
          <div className="w-full max-w-md rounded-lg border border-[var(--line)] bg-[var(--panel)] p-6 text-[var(--text)]">
            <h2 className="text-lg font-semibold">{dialog}</h2>
            <p className="mt-2 text-sm leading-6 text-[var(--muted)]">
              明暗は{themeLabel}です。タイトルバーへも同じ明暗を送っています。ボタンの文字はアクセントに対して黒か白です。
            </p>
            {dialog === "録音を保存しました" && saved && (
              <p className="mt-2 text-sm leading-6">
                {saved.path} を書きました。入力は {saved.inputRate} Hz、ヘッダは {saved.headerRate} Hz、{saved.bits} bit、{saved.channels} チャンネルです。ファイル {saved.seconds.toFixed(2)} 秒、操作 {saved.elapsed.toFixed(2)} 秒。
              </p>
            )}
            <button type="button" className="accent-button mt-4 w-full" onClick={() => setDialog(null)}>
              閉じる
            </button>
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
