import { useCallback, useEffect, useRef, useState } from "react"
import { bitmapRatio, canvasContext, shouldReplaceBitmap } from "./bitmap"
import { IconButton, IconNext, IconPlay, IconPrev, IconRecord, IconSkip, IconStop } from "./IconButton"
import { drawPeaks } from "./drawPeaks"
import { shownInputRate, type CaptureEvent, type PageCommand, type SavedEvent } from "./protocol"
import type { LiveBus } from "./live"
import { Segmented } from "./Segmented"

const notes = ["C3", "D3", "E3", "F3", "G3", "A3", "B3", "C4", "D4", "E4", "F4", "G4", "A4", "B4", "C5", "D5", "E5", "F5", "G5", "A5", "B5", "C6"]

export function RecPanel({
  bus,
  saved,
  status,
  waiting,
  advance,
  setAdvance,
  onPrev,
  onNext,
  onNextMissing,
  onRecord,
  blocked,
}: {
  bus: LiveBus
  saved: SavedEvent | null
  status: string
  waiting: boolean
  advance: boolean
  setAdvance: (value: boolean) => void
  onPrev: () => void
  onNext: () => void
  onNextMissing: () => void
  onRecord: (stop: boolean) => void
  blocked?: boolean
}) {
  const capture = useCapture(bus)
  const recording = capture?.recording ?? false
  const inputRate = shownInputRate(capture)
  return (
    <div className="flex h-full flex-col text-sm">
      <div className="flex flex-1 flex-col gap-3 overflow-auto p-3">
        {status && <p className="text-[var(--muted)]">{status}</p>}
        <p className="text-[var(--muted)]">
          入力 {inputRate == null ? "—" : `${inputRate} Hz`}
          {recording ? " / 録音中" : waiting ? " / カウント中" : ""}
        </p>
        <label className="flex gap-2">
          <input type="checkbox" checked={advance} onChange={(event) => setAdvance(event.target.checked)} />
          停止したあとに次の未録音へ進む
        </label>
        {saved && (
          <p className="leading-6">
            入力 {saved.inputRate} Hz、ヘッダ {saved.headerRate} Hz / {saved.bits} bit / {saved.channels} ch、
            ファイル {saved.seconds.toFixed(2)} 秒、操作 {saved.elapsed.toFixed(2)} 秒
          </p>
        )}
      </div>
      <div className="flex gap-2 border-t border-[var(--line)] p-3">
        <IconButton label="前へ" onClick={onPrev}><IconPrev /></IconButton>
        <IconButton label="次へ" onClick={onNext}><IconNext /></IconButton>
        <IconButton label="次の未録音" onClick={onNextMissing}><IconSkip /></IconButton>
        <IconButton
          label={recording || waiting ? "停止" : "録音"}
          accent
          disabled={Boolean(blocked) && !recording && !waiting}
          onClick={() => onRecord(recording || waiting)}
        >
          {recording || waiting ? <IconStop /> : <IconRecord />}
        </IconButton>
      </div>
    </div>
  )
}

export type GuideFileState = {
  manual: boolean
  path: string
  label: string
  recent: { path: string; label: string }[]
}

export function GuidePanel({
  guide,
  note,
  semitones,
  speed,
  volume,
  countIn,
  countBeats,
  countBeatsAfter,
  bpm,
  moras,
  guideWhile,
  recStartAfter,
  recEndBefore,
  playing,
  setNote,
  setSemitones,
  setSpeed,
  setVolume,
  setCountIn,
  setCountBeats,
  setCountBeatsAfter,
  setBpm,
  setMoras,
  setGuideWhile,
  setRecStartAfter,
  setRecEndBefore,
  send,
}: {
  guide: GuideFileState
  note: string
  semitones: number
  speed: number
  volume: number
  countIn: boolean
  countBeats: number
  countBeatsAfter: number
  bpm: number
  moras: number
  guideWhile: boolean
  recStartAfter: number
  recEndBefore: number
  playing: boolean
  setNote: (value: string) => void
  setSemitones: (value: number) => void
  setSpeed: (value: number) => void
  setVolume: (value: number) => void
  setCountIn: (value: boolean) => void
  setCountBeats: (value: number) => void
  setCountBeatsAfter: (value: number) => void
  setBpm: (value: number) => void
  setMoras: (value: number) => void
  setGuideWhile: (value: boolean) => void
  setRecStartAfter: (value: number) => void
  setRecEndBefore: (value: number) => void
  send: (command: PageCommand) => void
}) {
  const timingSliders = (
    <>
      <label className="grid gap-1">
        <span className="flex justify-between">
          録音開始位置
          <span className="num text-[var(--muted)]">{recStartAfter.toFixed(1)} s</span>
        </span>
        <input type="range" min={0} max={60} step={0.1} value={recStartAfter} onChange={(event) => setRecStartAfter(Number(event.target.value))} />
      </label>
      <label className="grid gap-1">
        <span className="flex justify-between">
          録音終了位置
          <span className="num text-[var(--muted)]">{recEndBefore.toFixed(1)} s</span>
        </span>
        <input type="range" min={0} max={60} step={0.1} value={recEndBefore} onChange={(event) => setRecEndBefore(Number(event.target.value))} />
      </label>
    </>
  )
  return (
    <div className="flex h-full flex-col text-sm">
      <div className="flex flex-1 flex-col gap-3 overflow-auto p-3">
        <label className="grid gap-1">
          <span className="flex justify-between">
            ガイドの大きさ
            <span className="num text-[var(--muted)]">{Math.round(volume * 100)}%</span>
          </span>
          <input type="range" min={0} max={1} step={0.01} value={volume} onChange={(event) => setVolume(Number(event.target.value))} />
        </label>
        <Segmented
          label="ガイドの種類"
          value={guide.manual ? "manual" : "file"}
          onChange={(value) => send({ type: "guideFile", manual: value === "manual" })}
          options={[
            ["file", "ファイルを選ぶ"],
            ["manual", "自分で設定する"],
          ]}
        />
        {guide.manual ? (
          <>
            <label className="grid gap-1">
              音程
              <select className="field" value={note} onChange={(event) => setNote(event.target.value)}>
                {notes.map((item) => (
                  <option key={item}>{item}</option>
                ))}
              </select>
            </label>
            <label className="grid gap-1">
              <span className="flex justify-between">
                モーラ数
                <span className="num text-[var(--muted)]">{moras}</span>
              </span>
              <input type="range" min={1} max={16} step={1} value={moras} onChange={(event) => setMoras(Number(event.target.value))} />
            </label>
            <label className="flex gap-2">
              <input type="checkbox" checked={countIn} onChange={(event) => setCountIn(event.target.checked)} />
              カウント
            </label>
            {countIn && (
              <>
                <label className="grid gap-1">
                  <span className="flex justify-between">
                    発声前のカウント数
                    <span className="num text-[var(--muted)]">{countBeats}</span>
                  </span>
                  <input type="range" min={1} max={16} step={1} value={countBeats} onChange={(event) => setCountBeats(Number(event.target.value))} />
                </label>
                <label className="grid gap-1">
                  <span className="flex justify-between">
                    発声後のカウント数
                    <span className="num text-[var(--muted)]">{countBeatsAfter}</span>
                  </span>
                  <input type="range" min={0} max={16} step={1} value={countBeatsAfter} onChange={(event) => setCountBeatsAfter(Number(event.target.value))} />
                </label>
              </>
            )}
            <label className="grid gap-1">
              <span className="flex justify-between">
                テンポ
                <span className="num text-[var(--muted)]">{bpm}</span>
              </span>
              <input type="range" min={40} max={240} step={1} value={bpm} onChange={(event) => setBpm(Number(event.target.value))} />
            </label>
            <label className="flex gap-2">
              <input type="checkbox" checked={guideWhile} onChange={(event) => setGuideWhile(event.target.checked)} />
              録音中のガイド
            </label>
            {timingSliders}
          </>
        ) : (
          <>
            <label className="grid gap-1">
              ファイル
              <select
                className="field"
                value={guide.path}
                onChange={(event) => send({ type: "guideFile", path: event.target.value })}
              >
                <option value="">選択しない</option>
                {guide.recent.map((item) => (
                  <option key={item.path} value={item.path}>{item.label}</option>
                ))}
                {guide.path && !guide.recent.some((item) => item.path === guide.path) && (
                  <option value={guide.path}>{guide.label || guide.path}</option>
                )}
              </select>
            </label>
            <button type="button" className="ghost-button" onClick={() => send({ type: "guideFile", pick: true })}>ファイルから選ぶ</button>
            <label className="grid gap-1">
              <span className="flex justify-between">
                再生速度
                <span className="num text-[var(--muted)]">{Math.round(speed * 100)}%</span>
              </span>
              <input type="range" min={0.5} max={2} step={0.05} value={speed} onChange={(event) => setSpeed(Number(event.target.value))} />
            </label>
            <label className="grid gap-1">
              <span className="flex justify-between">
                音程
                <span className="num text-[var(--muted)]">{semitones} 半音</span>
              </span>
              <input type="range" min={-12} max={12} step={1} value={semitones} onChange={(event) => setSemitones(Number(event.target.value))} />
            </label>
            {timingSliders}
          </>
        )}
      </div>
      <div className="flex border-t border-[var(--line)] p-3">
        <IconButton
          label={playing ? "停止" : "ガイドを聴く"}
          accent
          onClick={() => send(playing ? { type: "guideStop" } : { type: "guide", note, semitones, speed, volume, bpm, moras, countIn, countBeats, countBeatsAfter })}
        >
          {playing ? <IconStop /> : <IconPlay />}
        </IconButton>
      </div>
    </div>
  )
}

export function MicPanel({
  bus,
  appearance,
  monitor,
  setMonitor,
  recorded,
  takeName,
  wave,
  playing,
  position,
  send,
}: {
  bus: LiveBus
  appearance: string
  monitor: boolean
  setMonitor: (on: boolean) => void
  recorded: boolean
  takeName: string
  wave: { lo: number[]; hi: number[]; duration: number } | null
  playing: boolean
  position: number
  send: (command: PageCommand) => void
}) {
  const capture = useCapture(bus)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const bindCanvas = useCallback((node: HTMLCanvasElement | null) => {
    canvasRef.current = node
    if (node)
      canvasContext(node)
  }, [])
  const shown = recorded ? wave : null

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas)
      return
    const width = canvas.clientWidth
    const height = canvas.clientHeight
    if (width <= 0 || height <= 0)
      return
    const ratio = bitmapRatio()
    const nextWidth = Math.max(1, Math.floor(width * ratio))
    const nextHeight = Math.max(1, Math.floor(height * ratio))
    if (shouldReplaceBitmap(false, canvas.width, canvas.height, nextWidth, nextHeight)) {
      canvas.width = nextWidth
      canvas.height = nextHeight
    }
    const ctx = canvasContext(canvas)
    if (!ctx)
      return
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0)
    ctx.clearRect(0, 0, width, height)
    const peaks = shown ?? capture
    if (peaks)
      drawPeaks(ctx, width, height, peaks.lo, peaks.hi, getCss("--wave"))
    if (shown && shown.duration > 0 && playing) {
      const x = Math.min(width, Math.max(0, width * position / shown.duration))
      ctx.fillStyle = getCss("--accent")
      ctx.fillRect(x, 0, 2, height)
    }
  }, [capture, appearance, shown, playing, position])

  return (
    <div className="flex h-full flex-col text-sm">
      <div className="flex min-h-0 flex-1 flex-col gap-3 p-3">
        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={monitor}
            onChange={(event) => {
              setMonitor(event.target.checked)
              send({ type: "monitor", on: event.target.checked })
            }}
          />
          入力を聴く
        </label>
        {capture?.listenError && <p>{capture.listenError}</p>}
        <div className="relative min-h-16 flex-1 rounded border border-[var(--line)] bg-[var(--canvas)]">
          <canvas ref={bindCanvas} className="h-full w-full" />
        </div>
        <div className="h-2 overflow-hidden rounded bg-[var(--line)]">
          <div className="h-full bg-[var(--accent)]" style={{ width: `${Math.min(1, capture?.peak ?? 0) * 100}%` }} />
        </div>
      </div>
      <div className="flex border-t border-[var(--line)] p-3">
        <IconButton
          label={playing ? "停止" : "テイクを聴く"}
          accent
          disabled={!recorded || takeName.length === 0}
          onClick={() => send(playing ? { type: "take", op: "stop" } : { type: "take", op: "play", name: takeName })}
        >
          {playing ? <IconStop /> : <IconPlay />}
        </IconButton>
      </div>
    </div>
  )
}

function useCapture(bus: LiveBus) {
  const [capture, setCapture] = useState<CaptureEvent | null>(null)
  useEffect(() => bus.listen(setCapture), [bus])
  return capture
}

function getCss(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || "#222"
}
