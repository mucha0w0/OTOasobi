import { useState, type ReactNode } from "react"
import type { OtoKind, OtoState, PageCommand } from "./protocol"
import { Segmented } from "./Segmented"

type Send = (command: PageCommand) => void

export function OtoSettings({ oto, send }: { oto: OtoState | null; send: Send }) {
  const [naming, setNaming] = useState(false)
  const [name, setName] = useState("")
  if (!oto)
    return <p className="p-3 text-sm text-[var(--muted)]">設定を読み込んでいます。</p>
  const which = oto.mode === "cvvc" ? "cvvc" : "mono"
  return (
    <div className="h-full overflow-auto p-3">
      <Segmented
        label="モード"
        value={oto.mode}
        onChange={(mode) => send({ type: "oto", op: "mode", mode })}
        options={[
          ["mono", "単独音"],
          ["renzoku", "連続音"],
          ["cvvc", "CVVC"],
        ]}
      />
      {oto.busy && <p className="mt-3 text-sm text-[var(--muted)]">解析しています。</p>}
      {oto.mode === "mono" && (
        <div className="mt-3 grid gap-4">
          <Card title="プリセット">
            <PresetSelect
              names={oto.presets}
              value={oto.standalonePreset}
              dirty={oto.standaloneDirty}
              onChange={(next) => send({ type: "oto", op: "preset", target: "standalone", name: next })}
            />
            <button type="button" className="ghost-button w-full" onClick={() => { setName(""); setNaming(true) }}>
              現在の設定を保存
            </button>
          </Card>
          <Card title="区間">
            <Seconds label="オフセット" value={oto.offset} min={-0.1} max={0.1} onChange={(value) => slider(send, "mono", "offset", value)} />
            <Seconds label="子音部" value={oto.consonant} min={0} max={0.2} onChange={(value) => slider(send, "mono", "consonant", value)} />
            <Seconds label="ブランク" value={oto.blank} min={-0.1} max={0.1} onChange={(value) => slider(send, "mono", "blank", value)} />
          </Card>
          <Card title="オーバーラップ">
            <OverlapGroups kinds={oto.kinds} values={oto.overlap} allowSeconds onChange={(key, value) => slider(send, "monoOverlap", key, value)} />
          </Card>
        </div>
      )}
      {oto.mode === "renzoku" && (
        <div className="mt-3 grid gap-4">
          <Card title="無音の直後">
            <PresetSelect
              names={oto.presets}
              value={oto.restPreset}
              dirty={false}
              onChange={(next) => send({ type: "oto", op: "preset", target: "rest", name: next })}
            />
          </Card>
          <Card title="区間">
            <Seconds label="オフセット" value={oto.renzokuOffset} min={-0.1} max={0.1} onChange={(value) => slider(send, "renzoku", "offset", value)} />
            <Seconds label="子音部" value={oto.renzokuConsonant} min={0} max={0.2} onChange={(value) => slider(send, "renzoku", "consonant", value)} />
            <Seconds label="ブランク" value={oto.renzokuBlank} min={-0.1} max={0.1} onChange={(value) => slider(send, "renzoku", "blank", value)} />
          </Card>
          <Card title="オーバーラップ">
            <Percent label="割合" value={oto.renzokuOverlap} onChange={(value) => slider(send, "renzoku", "overlap", value)} />
          </Card>
        </div>
      )}
      {oto.mode === "cvvc" && (
        <div className="mt-3 grid gap-4">
          <Card title="先頭音素">
            <PresetSelect
              names={oto.presets}
              value={oto.leadPreset}
              dirty={false}
              onChange={(next) => send({ type: "oto", op: "preset", target: "lead", name: next })}
            />
          </Card>
          <Card title="プリセット">
            <PresetSelect
              names={oto.cvvcPresets}
              value={oto.cvvcPreset}
              dirty={oto.cvvcDirty}
              onChange={(next) => send({ type: "oto", op: "preset", target: "cvvc", name: next })}
            />
            <button type="button" className="ghost-button w-full" onClick={() => { setName(""); setNaming(true) }}>
              現在の設定を保存
            </button>
          </Card>
          <Card title="通常音素">
            <Seconds label="子音部" value={oto.cvvcConsonant} min={0} max={0.2} onChange={(value) => slider(send, "cvvc", "consonant", value)} />
            <Seconds label="ブランク" value={oto.cvvcBlank} min={-0.1} max={0.1} onChange={(value) => slider(send, "cvvc", "blank", value)} />
            <OverlapGroups kinds={oto.kinds} values={oto.cvvcOverlap} onChange={(key, value) => slider(send, "cvvcOverlap", key, value)} />
          </Card>
          <Card title="子音音素">
            <Seconds label="オフセット" value={oto.vcOffset} min={-0.1} max={0.1} onChange={(value) => slider(send, "cvvc", "vcOffset", value)} />
            <Percent label="オーバーラップ" value={oto.vcOverlap} onChange={(value) => slider(send, "cvvc", "vcOverlap", value)} />
            <p className="text-xs text-[var(--muted)]">子音部</p>
            <OverlapGroups kinds={oto.kinds} values={oto.vcConsonant} onChange={(key, value) => slider(send, "vcConsonant", key, value)} />
            <p className="text-xs text-[var(--muted)]">ブランク</p>
            <OverlapGroups kinds={oto.kinds} values={oto.vcBlank} onChange={(key, value) => slider(send, "vcBlank", key, value)} />
          </Card>
        </div>
      )}
      {naming && (
        <NameDialog
          title="現在の設定を保存"
          name={name}
          setName={setName}
          onCancel={() => setNaming(false)}
          onSave={() => {
            send({ type: "oto", op: "savePreset", which, name })
            setNaming(false)
          }}
        />
      )}
    </div>
  )
}

export function AnalyzePanel({
  busy,
  hasSelection,
  onAll,
  onSelected,
}: {
  busy: boolean
  hasSelection: boolean
  onAll: () => void
  onSelected: () => void
}) {
  return (
    <div className="flex h-full min-h-0 flex-col gap-2 p-3">
      <button type="button" className="accent-button w-full" disabled={busy} onClick={onAll}>
        すべてのファイルを解析
      </button>
      <button type="button" className="accent-button w-full" disabled={busy || !hasSelection} onClick={onSelected}>
        選択したファイルを解析
      </button>
    </div>
  )
}

export function OtoIni({ oto, send }: { oto: OtoState | null; send: Send }) {
  if (!oto)
    return <p className="p-3 text-sm text-[var(--muted)]">設定を読み込んでいます。</p>
  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="min-h-0 flex-1 overflow-auto p-3">
        <div className="grid gap-4">
          <Card title="生成するエイリアス">
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={oto.aliasMono}
                disabled={oto.aliasMonoLocked}
                onChange={(event) => send({ type: "oto", op: "alias", which: "mono", on: event.target.checked })}
              />
              単独音
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={oto.aliasRenzoku}
                onChange={(event) => send({ type: "oto", op: "alias", which: "renzoku", on: event.target.checked })}
              />
              連続音
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={oto.aliasCvvc}
                onChange={(event) => send({ type: "oto", op: "alias", which: "cvvc", on: event.target.checked })}
              />
              CVVC
            </label>
          </Card>
          <Card title="対象">
            <div className={oto.hasIni ? "grid gap-1" : "grid gap-1 opacity-45"}>
              {([
                ["all", "全てのエイリアスを上書きして生成する"],
                ["missing", "存在しないエイリアスだけ生成する"],
                ["overlap", "重複するエイリアスのみ上書きして生成する"],
              ] as const).map(([scope, label]) => (
                <label key={scope} className="flex items-start gap-2 text-sm">
                  <input
                    type="radio"
                    name="oto-scope"
                    checked={oto.scope === scope}
                    disabled={!oto.hasIni}
                    onChange={() => send({ type: "oto", op: "scope", scope })}
                  />
                  {label}
                </label>
              ))}
            </div>
          </Card>
        </div>
      </div>
      <div className="border-t border-[var(--line)] p-3">
        <button
          type="button"
          className="accent-button w-full"
          disabled={!oto.analyzed || oto.busy}
          onClick={() => send({ type: "oto", op: "write" })}
        >
          oto.iniを生成
        </button>
      </div>
    </div>
  )
}

function slider(send: Send, group: string, key: string, value: number) {
  send({ type: "oto", op: "slider", group, key, value })
}

function Card({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <section className="grid gap-2">
      <h3 className="section-title">{title}</h3>
      {children}
    </section>
  )
}

function PresetSelect({
  names,
  value,
  dirty,
  onChange,
}: {
  names: string[]
  value: string
  dirty: boolean
  onChange: (name: string) => void
}) {
  return (
    <label className="flex items-center gap-2 text-sm">
      <select className="field w-full" value={value} onChange={(event) => onChange(event.target.value)}>
        {names.map((name) => <option key={name} value={name}>{name}</option>)}
      </select>
      {dirty && <span title="このプリセットから変更しています">*</span>}
    </label>
  )
}

function Seconds({ label, value, min, max, onChange }: { label: string; value: number; min: number; max: number; onChange: (value: number) => void }) {
  return (
    <label className="grid gap-1 text-sm">
      <span className="flex justify-between">
        {label}
        <span className="num text-[var(--muted)]">{value.toFixed(3)} s</span>
      </span>
      <input
        type="range"
        min={min}
        max={max}
        step={0.001}
        value={value}
        aria-label={label}
        onChange={(event) => onChange(Number(event.target.value))}
      />
    </label>
  )
}

function Percent({ label, value, onChange }: { label: string; value: number; onChange: (value: number) => void }) {
  return (
    <label className="grid gap-1 text-sm">
      <span className="flex justify-between">
        {label}
        <span className="num text-[var(--muted)]">{Math.round(value)}%</span>
      </span>
      <input
        type="range"
        min={0}
        max={100}
        step={1}
        value={value}
        aria-label={label}
        onChange={(event) => onChange(Number(event.target.value))}
      />
    </label>
  )
}

function OverlapGroups({
  kinds,
  values,
  allowSeconds = false,
  onChange,
}: {
  kinds: OtoKind[]
  values: Record<string, number>
  allowSeconds?: boolean
  onChange: (key: string, value: number) => void
}) {
  let group = ""
  return (
    <div className="grid gap-3">
      {kinds.map((kind) => {
        const head = kind.group !== group
        group = kind.group
        const value = values[kind.key] ?? 50
        const seconds = allowSeconds && value < 0
        return (
          <div key={kind.key} className="grid gap-1">
            {head && <p className="text-xs text-[var(--muted)]">{kind.group}</p>}
            <label className="grid gap-1 text-sm">
              <span className="flex justify-between">
                {kind.caption}
                <span className="num text-[var(--muted)]">{seconds ? `${value.toFixed(3)} s` : `${Math.round(value)}%`}</span>
              </span>
              <input
                type="range"
                min={seconds ? -0.1 : 0}
                max={seconds ? 0 : 100}
                step={seconds ? 0.001 : 1}
                value={value}
                aria-label={`オーバーラップ ${kind.caption}`}
                onChange={(event) => onChange(kind.key, Number(event.target.value))}
                onPointerUp={(event) => {
                  if (!allowSeconds || seconds || value > 0)
                    return
                  const rect = event.currentTarget.getBoundingClientRect()
                  if (event.clientX < rect.left - 2)
                    onChange(kind.key, -0.001)
                }}
              />
            </label>
          </div>
        )
      })}
    </div>
  )
}

function NameDialog({
  title,
  name,
  setName,
  onCancel,
  onSave,
}: {
  title: string
  name: string
  setName: (name: string) => void
  onCancel: () => void
  onSave: () => void
}) {
  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-6">
      <form
        className="w-full max-w-sm rounded-md border border-[var(--line)] bg-[var(--panel)] p-4"
        onSubmit={(event) => {
          event.preventDefault()
          onSave()
        }}
      >
        <h2 className="text-base font-semibold">{title}</h2>
        <input className="field mt-3 w-full" value={name} placeholder="プリセット名" onChange={(event) => setName(event.target.value)} />
        <div className="mt-4 flex gap-2">
          <button type="button" className="ghost-button flex-1" onClick={onCancel}>キャンセル</button>
          <button type="submit" className="accent-button flex-1">保存</button>
        </div>
      </form>
    </div>
  )
}
