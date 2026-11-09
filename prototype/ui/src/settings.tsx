import { useState, type KeyboardEvent, type ReactNode } from "react"
import type { DeviceInfo } from "./protocol"
import type { ThemeChoice } from "./theme"

export type SettingsTab = {
  id: string
  title: string
  content: ReactNode
}

type LookModel = {
  theme: ThemeChoice
  accent: string
  scale: number
  lockPanels: boolean
  onTheme: (theme: ThemeChoice) => void
  onAccent: (hex: string) => void
  onScale: (scale: number) => void
  onLockPanels: (locked: boolean) => void
  onSystemAccent: () => void
}

type IoModel = {
  inputs: DeviceInfo[]
  outputs: DeviceInfo[]
  inputId: string
  outputId: string
  onInput: (id: string) => void
  onOutput: (id: string) => void
}

const themes = [
  { id: "light", label: "ライト" },
  { id: "dark", label: "ダーク" },
  { id: "system", label: "端末に合わせる" },
] as const

export function settingsTabs(look: LookModel, io: IoModel): SettingsTab[] {
  return [
    { id: "look", title: "見た目", content: <LookSettings {...look} /> },
    { id: "io", title: "入出力", content: <IoSettings {...io} /> },
  ]
}

export function SettingsDialog({ tabs, onClose }: { tabs: SettingsTab[]; onClose: () => void }) {
  const [picked, setPicked] = useState(tabs[0]?.id ?? "")
  const current = tabs.find((tab) => tab.id === picked) ?? tabs[0]
  const panelId = current ? `settings-${current.id}` : "settings-empty"

  function move(event: KeyboardEvent<HTMLDivElement>) {
    const index = tabs.findIndex((tab) => tab.id === current?.id)
    if (index < 0)
      return
    const step = event.key === "ArrowDown" || event.key === "ArrowRight"
      ? 1
      : event.key === "ArrowUp" || event.key === "ArrowLeft"
        ? -1
        : 0
    if (step === 0)
      return
    event.preventDefault()
    const next = tabs[(index + step + tabs.length) % tabs.length]
    setPicked(next.id)
    document.getElementById(`settings-tab-${next.id}`)?.focus()
  }

  return (
    <div className="fixed inset-0 z-30 flex items-center justify-center bg-black/40 p-6">
      <div role="dialog" aria-modal="true" aria-label="環境設定" className="flex h-[min(32rem,100%)] w-full max-w-3xl flex-col rounded-md border border-[var(--line)] bg-[var(--panel)] text-[var(--text)]">
        <div className="flex items-center justify-end border-b border-[var(--line)] py-2 pr-3 pl-5">
          <button
            type="button"
            className="grid size-7 shrink-0 place-items-center rounded text-[var(--muted)] hover:bg-[var(--line)]"
            aria-label="閉じる"
            title="閉じる"
            onClick={onClose}
          >
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
              <path d="M4 4 L12 12 M12 4 L4 12" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
            </svg>
          </button>
        </div>
        <div className="flex min-h-0 flex-1">
          <div role="tablist" aria-orientation="vertical" className="flex w-40 shrink-0 flex-col gap-1 border-r border-[var(--line)] p-3" onKeyDown={move}>
            {tabs.map((tab) => {
              const selected = tab.id === current?.id
              return (
                <button
                  key={tab.id}
                  type="button"
                  role="tab"
                  id={`settings-tab-${tab.id}`}
                  aria-selected={selected}
                  aria-controls={selected ? panelId : undefined}
                  tabIndex={selected ? 0 : -1}
                  className={`min-h-7 rounded px-3 py-1 text-left text-sm ${selected ? "is-picked" : "text-[var(--text)] hover:bg-[color-mix(in_srgb,var(--text)_8%,transparent)]"}`}
                  onClick={() => setPicked(tab.id)}
                >
                  {tab.title}
                </button>
              )
            })}
          </div>
          <div role="tabpanel" id={panelId} aria-labelledby={current ? `settings-tab-${current.id}` : undefined} className="min-h-0 flex-1 overflow-auto p-5">
            {current?.content}
          </div>
        </div>
      </div>
    </div>
  )
}

export function Setting({ label, children }: { label: string; children: ReactNode }) {
  return (
    <section className="grid gap-2">
      <h3 className="section-title">{label}</h3>
      {children}
    </section>
  )
}

function LookSettings({ theme, accent, scale, lockPanels, onTheme, onAccent, onScale, onLockPanels, onSystemAccent }: LookModel) {
  return (
    <div className="flex flex-col gap-4 text-sm">
      <Setting label="明暗">
        <fieldset className="flex flex-wrap gap-3">
          {themes.map((choice) => (
            <label key={choice.id} className="flex items-center gap-1">
              <input type="radio" name="theme" checked={theme === choice.id} onChange={() => onTheme(choice.id)} />
              {choice.label}
            </label>
          ))}
        </fieldset>
      </Setting>
      <Setting label="アクセント">
        <div className="flex flex-wrap items-center gap-2">
          <input type="color" value={accent} aria-label="アクセント" onChange={(event) => onAccent(event.target.value)} />
          <button type="button" className="ghost-button" onClick={onSystemAccent}>端末に合わせる</button>
        </div>
      </Setting>
      <Setting label="倍率">
        <label className="grid gap-1">
          <span className="num text-right text-[var(--muted)]">{Math.round(scale * 100)}%</span>
          <input
            type="range"
            min={50}
            max={200}
            step={10}
            aria-label="倍率"
            value={Math.round(scale * 100)}
            onChange={(event) => onScale(Number(event.target.value) / 100)}
          />
        </label>
      </Setting>
      <section className="grid gap-2">
        <label className="section-title flex items-center gap-2">
          <input type="checkbox" checked={lockPanels} onChange={(event) => onLockPanels(event.target.checked)} />
          パネルを固定
        </label>
      </section>
    </div>
  )
}

function IoSettings({ inputs, outputs, inputId, outputId, onInput, onOutput }: IoModel) {
  return (
    <div className="flex flex-col gap-4 text-sm">
      <Setting label="入力">
        <DeviceSelect label="入力" devices={inputs} value={inputId} onChange={onInput} />
      </Setting>
      <Setting label="出力">
        <DeviceSelect label="出力" devices={outputs} value={outputId} onChange={onOutput} />
      </Setting>
    </div>
  )
}

function DeviceSelect({ label, devices, value, onChange }: { label: string; devices: DeviceInfo[]; value: string; onChange: (id: string) => void }) {
  return (
    <select className="field w-full" aria-label={label} value={value} onChange={(event) => onChange(event.target.value)}>
      <option value="">既定</option>
      {devices.map((device) => <option key={device.id} value={device.id}>{device.label}</option>)}
    </select>
  )
}
