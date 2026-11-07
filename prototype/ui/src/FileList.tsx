type Props = {
  folder: string
  names: string[]
  selected: string
  onOpenFolder: () => void
  onSelect: (name: string) => void
}

export function FileList({ folder, names, selected, onOpenFolder, onSelect }: Props) {
  return (
    <aside className="flex w-52 shrink-0 flex-col border-r border-[var(--line)] bg-[var(--panel)]">
      <div className="flex flex-col gap-2 border-b border-[var(--line)] p-3">
        <button type="button" className="ghost-button w-full" onClick={onOpenFolder}>
          フォルダを開く
        </button>
        <p className="truncate text-xs text-[var(--muted)]" title={folder}>
          {folder ? folderName(folder) : "フォルダは未選択"}
        </p>
        <p className="text-xs text-[var(--muted)]">{names.length} 件</p>
      </div>
      <ul className="min-h-0 flex-1 overflow-auto py-1">
        {names.length === 0 && (
          <li className="px-3 py-2 text-sm text-[var(--muted)]">
            {folder ? "WAV はありません" : "フォルダを開いてください"}
          </li>
        )}
        {names.map((name) => {
          const current = name.localeCompare(selected, undefined, { sensitivity: "accent" }) === 0
          return (
            <li key={name}>
              <button
                type="button"
                title={name}
                className={`block w-full truncate px-3 py-1 text-left text-sm ${current ? "bg-[var(--accent)] text-[var(--on-accent)]" : "text-[var(--text)]"}`}
                onClick={() => onSelect(name)}
              >
                {name}
              </button>
            </li>
          )
        })}
      </ul>
    </aside>
  )
}

function folderName(folder: string): string {
  const parts = folder.split(/[\\/]/).filter(Boolean)
  return parts[parts.length - 1] ?? folder
}
