type Props = {
  folder: string
  names: string[]
  selected: string
  onSelect: (name: string) => void
}

export function FileList({ folder, names, selected, onSelect }: Props) {
  return (
    <ul className="h-full overflow-auto py-1">
      {names.length === 0 && folder && (
        <li className="px-3 py-2 text-sm text-[var(--muted)]">WAV はありません</li>
      )}
      {names.map((name) => {
        const current = name.localeCompare(selected, undefined, { sensitivity: "accent" }) === 0
        return (
          <li key={name}>
            <button
              type="button"
              title={name}
              className={`block w-full truncate px-3 py-1 text-left text-sm ${current ? "is-picked" : "text-[var(--text)]"}`}
              onClick={() => onSelect(name)}
            >
              {name}
            </button>
          </li>
        )
      })}
    </ul>
  )
}
