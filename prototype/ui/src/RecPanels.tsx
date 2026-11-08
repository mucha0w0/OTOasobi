import type { RecLineInfo, RecListEvent } from "./protocol"
import { Segmented } from "./Segmented"

export function RecListPanel({
  list,
  filter,
  selected,
  setFilter,
  onChoose,
  onPick,
  onOpen,
  onSelect,
}: {
  list: RecListEvent | null
  filter: "all" | "todo" | "done" | "extra"
  selected: number | null
  setFilter: (filter: "all" | "todo" | "done" | "extra") => void
  onChoose: () => void
  onPick: () => void
  onOpen: (path: string) => void
  onSelect: (index: number | null, wav: string) => void
}) {
  const lines = list?.lines ?? []
  const recorded = lines.filter((line) => line.recorded).length
  const shown = filter === "todo"
    ? lines.map((line, index) => ({ line, index })).filter((item) => !item.line.recorded && !item.line.error)
    : filter === "done"
      ? lines.map((line, index) => ({ line, index })).filter((item) => item.line.recorded)
      : filter === "extra"
        ? []
        : lines.map((line, index) => ({ line, index }))
  const options = list?.recent ?? []
  const current = list?.path ?? ""
  const known = current.length > 0 && options.some((item) => item.path.toLowerCase() === current.toLowerCase())
  return (
    <div className="flex h-full min-h-0 flex-col text-sm">
      <div className="flex flex-col gap-2 p-3">
        <h2 className="font-semibold">録音リスト</h2>
        <select
          className="field"
          value={current}
          onChange={(event) => {
            const value = event.target.value
            if (value === "__pick")
              onPick()
            else if (value === "")
              onChoose()
            else
              onOpen(value)
          }}
        >
          <option value="">選択しない</option>
          <option value="__pick">ファイルから選ぶ</option>
          {current.length > 0 && !known && <option value={current}>{current.split(/[/\\]/).pop()}</option>}
          {options.map((item) => (
            <option key={item.path} value={item.path}>{item.label}</option>
          ))}
        </select>
        <p className="text-[var(--muted)]">録音済み {recorded} / {lines.length}</p>
        <Segmented
          label="絞り込み"
          value={filter}
          onChange={setFilter}
          options={[
            ["all", "すべて"],
            ["todo", "未録音"],
            ["done", "録音済み"],
            ["extra", "リスト外"],
          ]}
        />
      </div>
      <div className="min-h-0 flex-1 overflow-auto border-t border-[var(--line)]">
        {filter === "extra" ? (
          (list?.outside.length ?? 0) === 0
            ? <p className="p-3 text-[var(--muted)]">リストに無い WAV はありません。</p>
            : list?.outside.map((wav) => (
              <button key={wav} type="button" className="block w-full px-3 py-2 text-left hover:bg-[var(--line)]" onClick={() => onSelect(null, wav)}>
                {wav}
                <span className="mt-1 block text-xs text-[var(--muted)]">リスト外</span>
              </button>
            ))
        ) : shown.map(({ line, index }) => (
          <button
            key={`${line.wav}-${index}`}
            type="button"
            className={`block w-full px-3 py-2 text-left ${selected === index ? "is-picked" : "hover:bg-[var(--line)]"}`}
            onClick={() => onSelect(index, line.wav)}
          >
            <span className="block truncate">{line.sing}</span>
            <span className="mt-1 block truncate text-xs text-[var(--muted)]">{line.error ?? (line.recorded ? "録音済み" : "未録音")}{line.comment ? `　${line.comment}` : ""}</span>
          </button>
        ))}
      </div>
    </div>
  )
}

export function RecScriptPanel({
  line,
  next,
  outside,
}: {
  line: RecLineInfo | null
  next: string
  outside: string
}) {
  const title = line?.sing || outside || ""
  return (
    <div className="flex h-full flex-col justify-center gap-3 p-4">
      <p className="text-4xl leading-tight">{title}</p>
      {line?.comment && <p className="text-[var(--muted)]">{line.comment}</p>}
      {line?.error && <p className="text-[var(--muted)]">{line.error}</p>}
      {next && <p className="text-xl text-[var(--muted)]">次　{next}</p>}
    </div>
  )
}
