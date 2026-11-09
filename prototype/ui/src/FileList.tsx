import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from "react"
import { aliasId } from "./aliasList"
import { sameName } from "./filePick"
import { aliasText } from "./otoSheet"
import type { OtoMark } from "./protocol"

type PickItem = { id: string; label: string }

type PickProps = {
  items: PickItem[]
  empty: string
  selected: string[]
  current: string
  onSelect: (id: string, shift: boolean, ctrl: boolean) => void
  onSelectAll: () => void
}

export type FileListHandle = {
  highlight: (id: string) => void
  release: () => void
}

const PickList = forwardRef<FileListHandle, PickProps>(function PickList({ items, empty, selected, current, onSelect, onSelectAll }, ref) {
  const currentRef = useRef<HTMLButtonElement>(null)
  const hold = useRef(false)
  const [shown, setShown] = useState(current)
  if (!hold.current && shown !== current)
    setShown(current)
  useImperativeHandle(ref, () => ({
    highlight(id: string) {
      hold.current = true
      setShown(id)
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
      {items.length === 0 && empty && (
        <li className="px-3 py-2 text-sm text-[var(--muted)]">{empty}</li>
      )}
      {items.map((item) => {
        const marked = sameName(item.id, shown)
        const picked = selected.some((id) => sameName(id, item.id))
        return (
          <li key={item.id}>
            <button
              ref={marked ? currentRef : undefined}
              type="button"
              title={item.label}
              aria-current={marked ? "true" : undefined}
              aria-selected={picked || marked}
              className={`block w-full truncate px-3 py-1 text-left text-sm ${marked ? "file-current" : picked ? "file-selected" : "text-[var(--text)] hover:bg-[color-mix(in_srgb,var(--text)_8%,transparent)]"}`}
              onClick={(event) => onSelect(item.id, event.shiftKey, event.ctrlKey || event.metaKey)}
            >
              {item.label}
            </button>
          </li>
        )
      })}
    </ul>
  )
})

type FileProps = {
  folder: string
  names: string[]
  selected: string[]
  current: string
  onSelect: (name: string, shift: boolean, ctrl: boolean) => void
  onSelectAll: () => void
}

export const FileList = forwardRef<FileListHandle, FileProps>(function FileList({ folder, names, selected, current, onSelect, onSelectAll }, ref) {
  return (
    <PickList
      ref={ref}
      items={names.map((name) => ({ id: name, label: name }))}
      empty={folder ? "WAV はありません" : ""}
      selected={selected}
      current={current}
      onSelect={onSelect}
      onSelectAll={onSelectAll}
    />
  )
})

type AliasProps = {
  folder: string
  rows: OtoMark[]
  selected: string[]
  current: string
  onSelect: (id: string, shift: boolean, ctrl: boolean) => void
  onSelectAll: () => void
}

export const AliasList = forwardRef<FileListHandle, AliasProps>(function AliasList({ folder, rows, selected, current, onSelect, onSelectAll }, ref) {
  return (
    <PickList
      ref={ref}
      items={rows.map((row) => ({ id: aliasId(row), label: aliasText(row) }))}
      empty={folder ? "エイリアスはありません" : ""}
      selected={selected}
      current={current}
      onSelect={onSelect}
      onSelectAll={onSelectAll}
    />
  )
})
