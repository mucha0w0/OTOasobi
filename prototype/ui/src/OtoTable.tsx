import { useState } from "react"
import { otoField } from "./otoFields"
import type { OtoMark, PageCommand } from "./protocol"

const headers = ["ファイル", "エイリアス", "オフセット", "オーバーラップ", "先行発声", "子音部", "ブランク", "メモ"]

export function OtoTable({
  rows,
  send,
}: {
  rows: OtoMark[]
  send: (command: PageCommand) => void
}) {
  if (rows.length === 0)
    return <p className="p-3 text-sm text-[var(--muted)]">解析が終わると、計算した oto.ini の数値がここに出ます。</p>
  return (
    <div className="h-full overflow-auto">
      <table className="w-full border-collapse text-left text-sm">
        <thead>
          <tr>
            {headers.map((header) => (
              <th key={header} className="sticky top-0 border-b border-[var(--line)] bg-[var(--panel)] px-2 py-2 font-medium">{header}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((mark) => (
            <tr key={`${mark.file}:${mark.index}:${mark.autoAlias}`}>
              <td className="border-b border-[var(--line)] px-2 py-1">{mark.file}</td>
              <td className="border-b border-[var(--line)] px-2 py-1">
                <Cell
                  value={mark.name || mark.autoAlias}
                  star={mark.name.length > 0 || mark.tuned.some(Boolean)}
                  onCommit={(name) => send({ type: "oto", op: "aliasName", file: mark.file, index: mark.index, alias: mark.autoAlias, name })}
                />
              </td>
              {[0, 1, 2, 3, 4].map((point) => (
                <td key={point} className="border-b border-[var(--line)] px-2 py-1">
                  <Cell
                    value={otoField(mark, point).toFixed(3)}
                    star={mark.tuned[point] === true}
                    onCommit={(text) => {
                      const next = Number(text)
                      if (!Number.isFinite(next))
                        return
                      const fields = [0, 1, 2, 3, 4].map((item) => item === point ? next : otoField(mark, item))
                      send({
                        type: "oto",
                        op: "fields",
                        file: mark.file,
                        index: mark.index,
                        alias: mark.autoAlias,
                        offset: fields[0] ?? 0,
                        overlap: fields[1] ?? 0,
                        preutter: fields[2] ?? 0,
                        consonant: fields[3] ?? 0,
                        blank: fields[4] ?? 0,
                      })
                    }}
                  />
                </td>
              ))}
              <td className="border-b border-[var(--line)] px-2 py-1">
                <Cell
                  value={mark.comment}
                  star={false}
                  onCommit={(comment) => send({ type: "oto", op: "comment", file: mark.file, index: mark.index, alias: mark.autoAlias, comment })}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function Cell({ value, star, onCommit }: { value: string; star: boolean; onCommit: (value: string) => void }) {
  const [text, setText] = useState(value)
  const [editing, setEditing] = useState(false)
  const shown = editing ? text : value
  return (
    <label className="flex items-center gap-1">
      <input
        className="field w-full"
        value={shown}
        onFocus={() => {
          setText(value)
          setEditing(true)
        }}
        onChange={(event) => setText(event.target.value)}
        onBlur={() => {
          setEditing(false)
          if (text !== value)
            onCommit(text)
        }}
        onKeyDown={(event) => {
          if (event.key === "Enter")
            event.currentTarget.blur()
        }}
      />
      {star && <span>*</span>}
    </label>
  )
}
