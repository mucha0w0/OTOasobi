import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from "react"
import { sameName } from "./filePick"

type Props = {
  folder: string
  names: string[]
  selected: string[]
  current: string
  onSelect: (name: string, shift: boolean, ctrl: boolean) => void
  onSelectAll: () => void
}

export type FileListHandle = {
  highlight: (name: string) => void
  release: () => void
}

export const FileList = forwardRef<FileListHandle, Props>(function FileList({ folder, names, selected, current, onSelect, onSelectAll }, ref) {
  const currentRef = useRef<HTMLButtonElement>(null)
  const hold = useRef(false)
  const [shown, setShown] = useState(current)
  if (!hold.current && shown !== current)
    setShown(current)
  useImperativeHandle(ref, () => ({
    highlight(name: string) {
      hold.current = true
      setShown(name)
    },
    release() {
      hold.current = false
      setShown(current)
    },
  }), [current])
  useEffect(() => {
    currentRef.current?.scrollIntoView({ block: "nearest" })
  }, [shown])
  return (
    <ul
      className="h-full select-none overflow-auto py-1"
      onKeyDown={(event) => {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "a") {
          event.preventDefault()
          onSelectAll()
        }
      }}
    >
      {names.length === 0 && folder && (
        <li className="px-3 py-2 text-sm text-[var(--muted)]">WAV はありません</li>
      )}
      {names.map((name) => {
        const marked = name.localeCompare(shown, undefined, { sensitivity: "accent" }) === 0
        const picked = selected.some((item) => sameName(item, name))
        return (
          <li key={name}>
            <button
              ref={marked ? currentRef : undefined}
              type="button"
              title={name}
              aria-current={marked ? "true" : undefined}
              aria-selected={picked || marked}
              className={`block w-full truncate px-3 py-1 text-left text-sm ${marked ? "file-current" : picked ? "file-selected" : "text-[var(--text)] hover:bg-[color-mix(in_srgb,var(--text)_8%,transparent)]"}`}
              onClick={(event) => onSelect(name, event.shiftKey, event.ctrlKey || event.metaKey)}
            >
              {name}
            </button>
          </li>
        )
      })}
    </ul>
  )
})
