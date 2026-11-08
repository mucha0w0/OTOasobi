import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from "react"

type Props = {
  folder: string
  names: string[]
  selected: string
  onSelect: (name: string) => void
}

export type FileListHandle = {
  highlight: (name: string) => void
  release: () => void
}

export const FileList = forwardRef<FileListHandle, Props>(function FileList({ folder, names, selected, onSelect }, ref) {
  const currentRef = useRef<HTMLButtonElement>(null)
  const hold = useRef(false)
  const [shown, setShown] = useState(selected)
  if (!hold.current && shown !== selected)
    setShown(selected)
  useImperativeHandle(ref, () => ({
    highlight(name: string) {
      hold.current = true
      setShown(name)
    },
    release() {
      hold.current = false
      setShown(selected)
    },
  }), [selected])
  useEffect(() => {
    currentRef.current?.scrollIntoView({ block: "nearest" })
  }, [shown])
  return (
    <ul className="h-full overflow-auto py-1">
      {names.length === 0 && folder && (
        <li className="px-3 py-2 text-sm text-[var(--muted)]">WAV はありません</li>
      )}
      {names.map((name) => {
        const current = name.localeCompare(shown, undefined, { sensitivity: "accent" }) === 0
        return (
          <li key={name}>
            <button
              ref={current ? currentRef : undefined}
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
})
