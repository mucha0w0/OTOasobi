import { useState } from "react"
import type { PageCommand } from "./protocol"

export function FrqPanel({
  wavs,
  busy,
  hasFile,
  selectedNames,
  send,
}: {
  wavs: number
  busy: boolean
  hasFile: boolean
  selectedNames: string[]
  send: (command: PageCommand) => void
}) {
  const [mode, setMode] = useState<"all" | "missing" | "selected">("all")
  const [preutter, setPreutter] = useState<"keep" | "zero" | "none">("keep")
  return (
    <div className="flex h-full min-h-0 flex-col text-sm">
      <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-auto p-3">
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
            選択中のファイルだけ生成する
          </label>
        </section>
        <section className="grid gap-2">
          <h2 className="section-title">先行発声</h2>
          <label className="mt-2 flex gap-2">
            <input type="radio" name="frq-preutter" checked={preutter === "keep"} onChange={() => setPreutter("keep")} />
            なにもしない
          </label>
          <label className="mt-2 flex gap-2">
            <input type="radio" name="frq-preutter" checked={preutter === "zero"} onChange={() => setPreutter("zero")} />
            先行発声までを０に統一する
          </label>
          <label className="mt-2 flex gap-2">
            <input type="radio" name="frq-preutter" checked={preutter === "none"} onChange={() => setPreutter("none")} />
            先行発声までを「なし」にする
          </label>
        </section>
      </div>
      <div className="border-t border-[var(--line)] p-3">
        <button
          type="button"
          className="accent-button w-full"
          disabled={busy || wavs === 0 || (mode === "selected" && !hasFile)}
          onClick={() => send({ type: "frq", op: "write", mode, clear: preutter !== "keep", names: mode === "selected" ? selectedNames : [] })}
        >
          .frqを生成
        </button>
      </div>
    </div>
  )
}
