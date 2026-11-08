import { useState } from "react"
import type { PageCommand } from "./protocol"

export function FrqPanel({
  wavs,
  frqs,
  busy,
  hasFile,
  send,
}: {
  wavs: number
  frqs: number
  busy: boolean
  hasFile: boolean
  send: (command: PageCommand) => void
}) {
  const [mode, setMode] = useState<"all" | "missing" | "selected">("all")
  const [clear, setClear] = useState(false)
  const summary = wavs === 0
    ? "フォルダ直下に WAV がありません。"
    : `WAV ${wavs} 件のうち、.frq は ${frqs} 件あります。`
  return (
    <div className="flex h-full min-h-0 flex-col text-sm">
      <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-auto p-3">
        <section className="grid gap-2">
          <h2 className="section-title">周波数表</h2>
          <p className="mt-1 leading-6 text-[var(--muted)]">
            フォルダ直下の各 WAV と同じ名前で、UTAU 用の .frq を書きます。256 サンプルごとの高さと、有声部分の平均です。
          </p>
          <p className="mt-2 text-[var(--muted)]">{summary}</p>
        </section>
        <section className="grid gap-2">
          <h2 className="section-title">対象</h2>
          <label className="mt-2 flex gap-2">
            <input type="radio" name="frq-mode" checked={mode === "all"} onChange={() => setMode("all")} />
            全てのfrqファイルを上書きして生成する
          </label>
          <label className="mt-2 flex gap-2">
            <input type="radio" name="frq-mode" checked={mode === "missing"} onChange={() => setMode("missing")} />
            frqが存在しないファイルだけ生成する
          </label>
          <label className="mt-2 flex gap-2">
            <input type="radio" name="frq-mode" checked={mode === "selected"} onChange={() => setMode("selected")} />
            現在選択中のファイルだけ生成する
          </label>
        </section>
        <section className="grid gap-2">
          <label className="flex gap-2">
            <input type="checkbox" checked={clear} onChange={(event) => setClear(event.target.checked)} />
            先行発声までを０に統一する
          </label>
          <p className="mt-1 pl-6 text-[var(--muted)]">これは単独音のときのみオンにしてください。</p>
        </section>
      </div>
      <div className="border-t border-[var(--line)] p-3">
        <button
          type="button"
          className="accent-button w-full"
          disabled={busy || wavs === 0 || (mode === "selected" && !hasFile)}
          onClick={() => send({ type: "frq", op: "write", mode, clear })}
        >
          .frqを生成
        </button>
      </div>
    </div>
  )
}
