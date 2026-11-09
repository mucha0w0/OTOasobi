import { useEffect, useState, type ReactNode } from "react"
import { sameName } from "./filePick"
import { ToneRow } from "./notice"
import { formatNoiseSeconds } from "./noiseWave"
import { NoiseWaveform } from "./NoiseWaveform"
import type { EffectsEvent, NoiseWaveEvent, PageCommand } from "./protocol"

const bands = [63, 125, 250, 500, 1000, 2000, 4000, 8000]

export function EffectsPanel({
  effects,
  wave,
  appearance,
  busy,
  send,
}: {
  effects: EffectsEvent | null
  wave: NoiseWaveEvent | null
  appearance: string
  busy: boolean
  send: (command: PageCommand) => void
}) {
  const [form, setForm] = useState(effects)
  const [confirm, setConfirm] = useState(false)
  const [blocked, setBlocked] = useState("")
  const [draft, setDraft] = useState<{ start: number; end: number } | null>(null)
  useEffect(() => {
    setForm(effects)
    if (!effects?.noiseMessage)
      setDraft(null)
  }, [effects])
  if (!form)
    return <p className="p-3 text-sm text-[var(--muted)]">エフェクトの設定を読み込んでいます。</p>

  function push(next: EffectsEvent) {
    setForm({ ...next, preset: "カスタム" })
    send({
      type: "effects",
      op: "set",
      noiseEnabled: next.noiseEnabled,
      noiseReduction: next.noiseReduction,
      highpassEnabled: next.highpassEnabled,
      highpassHz: next.highpassHz,
      highpassSlope: next.highpassSlope,
      eqEnabled: next.eqEnabled,
      gains: next.gains,
      compressorEnabled: next.compressorEnabled,
      compressorRatio: next.compressorRatio,
      compressorReduction: next.compressorReduction,
      normalizeEnabled: next.normalizeEnabled,
      normalizeTarget: next.normalizeTarget,
    })
  }

  return (
    <div className="flex h-full min-h-0 min-w-0 flex-col text-sm">
      <div className="flex min-h-0 min-w-0 flex-1 flex-col gap-4 overflow-y-auto overflow-x-hidden p-3">
        <label className="grid min-w-0 gap-1">
          プリセット
          <select className="field min-w-0 w-full" value={form.preset} onChange={(event) => send({ type: "effects", op: "preset", name: event.target.value })}>
            <option value="カスタム">カスタム</option>
            {form.presets.filter((name) => name !== "カスタム").map((name) => <option key={name}>{name}</option>)}
          </select>
        </label>
        <Block title="ノイズサプレッサー" on={form.noiseEnabled} setOn={(noiseEnabled) => push({ ...form, noiseEnabled })}>
          <Slider label="抑える量" min={0} max={16} step={0.5} value={form.noiseReduction} suffix=" dB" onChange={(noiseReduction) => push({ ...form, noiseReduction })} />
          <label className="mt-2 grid gap-1">
            原音
            <select
              className="field min-w-0 w-full"
              value={form.noiseFile}
              disabled={noiseFiles(form).length === 0 && !form.noiseFile}
              onChange={(event) => {
                const file = event.target.value
                setDraft(null)
                setForm({ ...form, noiseFile: file, noiseStart: 0, noiseEnd: 0, noiseReady: false, noiseMessage: null })
                send({ type: "effects", op: "noise", file })
              }}
            >
              <option value="">{noiseFiles(form).length === 0 && !form.noiseFile ? "WAV がありません" : "選択しない"}</option>
              {noiseFiles(form).map((name) => <option key={name}>{name}</option>)}
            </select>
          </label>
          {form.noiseFile && (
            <NoiseBody
              form={form}
              wave={wave}
              appearance={appearance}
              draft={draft}
              setDraft={setDraft}
              send={send}
            />
          )}
          {form.noiseMessage && <p className="text-[var(--danger)]">{form.noiseMessage}</p>}
        </Block>
        <Block title="ハイパス" on={form.highpassEnabled} setOn={(highpassEnabled) => push({ ...form, highpassEnabled })}>
          <Slider label="周波数" min={20} max={200} step={1} value={form.highpassHz} suffix=" Hz" onChange={(highpassHz) => push({ ...form, highpassHz })} />
          <label className="mt-2 flex gap-3">
            <span><input type="radio" checked={form.highpassSlope === 12} onChange={() => push({ ...form, highpassSlope: 12 })} /> 12 dB/oct</span>
            <span><input type="radio" checked={form.highpassSlope === 24} onChange={() => push({ ...form, highpassSlope: 24 })} /> 24 dB/oct</span>
          </label>
        </Block>
        <Block title="イコライザー" on={form.eqEnabled} setOn={(eqEnabled) => push({ ...form, eqEnabled })}>
          {bands.map((hz, index) => (
            <Slider
              key={hz}
              label={hz >= 1000 ? `${hz / 1000} kHz` : `${hz} Hz`}
              min={-12}
              max={12}
              step={0.1}
              value={form.gains[index] ?? 0}
              suffix=" dB"
              onChange={(value) => {
                const gains = form.gains.slice(0, 8)
                while (gains.length < 8)
                  gains.push(0)
                gains[index] = value
                push({ ...form, gains })
              }}
            />
          ))}
        </Block>
        <Block title="コンプレッサー" on={form.compressorEnabled} setOn={(compressorEnabled) => push({ ...form, compressorEnabled })}>
          <Slider label="比" min={2} max={3} step={0.1} value={form.compressorRatio} suffix="" onChange={(compressorRatio) => push({ ...form, compressorRatio })} />
          <Slider label="下げ幅" min={0} max={6} step={0.1} value={form.compressorReduction} suffix=" dB" onChange={(compressorReduction) => push({ ...form, compressorReduction })} />
        </Block>
        <Block title="ノーマライズ" on={form.normalizeEnabled} setOn={(normalizeEnabled) => push({ ...form, normalizeEnabled })}>
          <Slider label="目標" min={-12} max={0} step={0.1} value={form.normalizeTarget} suffix=" dB" onChange={(normalizeTarget) => push({ ...form, normalizeTarget })} />
        </Block>
      </div>
      <div className="border-t border-[var(--line)] p-3">
        {blocked && <ToneRow tone="error" className="mb-2">{blocked}</ToneRow>}
        <button type="button" className="accent-button w-full" disabled={busy} onClick={() => {
          if (form.noiseEnabled && !form.noiseReady) {
            setBlocked("ノイズサプレッサーがオンです。原音から無音の範囲を選んでから、適用してください。")
            return
          }
          setBlocked("")
          setConfirm(true)
        }}>適用</button>
      </div>
      {confirm && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
          <div className="w-full max-w-md rounded-md border border-[var(--line)] bg-[var(--panel)] p-4">
            <h2 className="text-base font-semibold">解析データはリセットされますがよろしいですか？</h2>
            <p className="mt-2 text-sm">original の音声に、オンのエフェクトをかけて、フォルダ直下の音声を上書きします。そのあと解析します。</p>
            {form.noiseNote && <p className="mt-2 text-sm">{form.noiseNote}</p>}
            <div className="mt-4 flex gap-2">
              <button type="button" className="ghost-button flex-1" onClick={() => setConfirm(false)}>いいえ</button>
              <button type="button" className="accent-button flex-1" onClick={() => { setConfirm(false); send({ type: "effects", op: "apply" }) }}>はい</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function noiseFiles(form: EffectsEvent) {
  return form.noiseFile && !form.noiseFiles.includes(form.noiseFile)
    ? [...form.noiseFiles, form.noiseFile]
    : form.noiseFiles
}

function NoiseBody({
  form,
  wave,
  appearance,
  draft,
  setDraft,
  send,
}: {
  form: EffectsEvent
  wave: NoiseWaveEvent | null
  appearance: string
  draft: { start: number; end: number } | null
  setDraft: (draft: { start: number; end: number } | null) => void
  send: (command: PageCommand) => void
}) {
  const shown = wave && sameName(wave.file, form.noiseFile) ? wave : null
  const range = draft ?? (form.noiseReady ? { start: form.noiseStart, end: form.noiseEnd } : null)
  return (
    <div className="mt-2 grid min-w-0 gap-1">
      {shown && shown.lo.length > 0 ? (
        <NoiseWaveform
          wave={shown}
          start={range?.start ?? null}
          end={range?.end ?? null}
          appearance={appearance}
          onChanging={(start, end) => setDraft({ start, end })}
          onChosen={(start, end) => {
            setDraft({ start, end })
            send({ type: "effects", op: "noise", file: form.noiseFile, start, end })
          }}
        />
      ) : (
        <p className="text-[var(--muted)]">読み込み中</p>
      )}
      {range && <p className="num text-[var(--muted)]">{formatNoiseSeconds(range.start, range.end)}</p>}
    </div>
  )
}

function Block({
  title,
  on,
  setOn,
  children,
}: {
  title: string
  on: boolean
  setOn: (on: boolean) => void
  children: ReactNode
}) {
  return (
    <section className="grid min-w-0 gap-2">
      <label className="section-title flex items-center gap-2">
        <input type="checkbox" checked={on} onChange={(event) => setOn(event.target.checked)} />
        {title}
      </label>
      <div>{children}</div>
    </section>
  )
}

function Slider({
  label,
  min,
  max,
  step,
  value,
  suffix,
  onChange,
}: {
  label: string
  min: number
  max: number
  step: number
  value: number
  suffix: string
  onChange: (value: number) => void
}) {
  return (
    <label className="mt-2 grid gap-1">
      <span className="flex justify-between">
        {label}
        <span className="num text-[var(--muted)]">{Number.isFinite(value) ? value.toFixed(step < 1 ? 1 : 0) : ""}{suffix}</span>
      </span>
      <input type="range" min={min} max={max} step={step} value={value} onChange={(event) => onChange(Number(event.target.value))} />
    </label>
  )
}
