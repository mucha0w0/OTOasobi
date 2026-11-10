import { forwardRef, useEffect, useImperativeHandle, useRef } from "react"
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

const rowClass = "block w-full truncate px-3 py-1 text-left text-sm"
const currentClass = `${rowClass} file-current`
const selectedClass = `${rowClass} file-selected`
const idleClass = `${rowClass} text-[var(--text)] hover:bg-[color-mix(in_srgb,var(--text)_8%,transparent)]`

const PickList = forwardRef<FileListHandle, PickProps>(function PickList({ items, empty, selected, current, onSelect, onSelectAll }, ref) {
  const listRef = useRef<HTMLUListElement>(null)
  const nodes = useRef(new Map<string, HTMLButtonElement>())
  const hold = useRef(false)
  const shownRef = useRef(current)
  if (!hold.current)
    shownRef.current = current
  const shown = shownRef.current
  useImperativeHandle(ref, () => ({
    highlight(id: string) {
      hold.current = true
      if (shownRef.current === id)
        return
      const previous = nodes.current.get(shownRef.current)
      const next = nodes.current.get(id)
      if (previous)
        previous.className = previous.dataset.idle ?? idleClass
      if (next) {
        next.className = currentClass
        revealRow(next)
      }
      shownRef.current = id
    },
    release() {
      hold.current = false
      shownRef.current = current
    },
  }), [current])
  useEffect(() => {
    const row = listRef.current?.querySelector<HTMLButtonElement>(".file-current")
    if (row)
      revealRow(row)
  }, [shown])
  return (
    <ul
      ref={listRef}
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
        const idle = picked ? selectedClass : idleClass
        return (
          <li key={item.id}>
            <button
              ref={(node) => {
                if (node)
                  nodes.current.set(item.id, node)
                else
                  nodes.current.delete(item.id)
              }}
              type="button"
              data-idle={idle}
              title={item.label}
              aria-current={marked ? "true" : undefined}
              aria-selected={picked || marked}
              className={marked ? currentClass : idle}
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

function revealRow(row: HTMLElement) {
  const list = row.closest("ul")
  if (!list)
    return
  const listBox = list.getBoundingClientRect()
  const rowBox = row.getBoundingClientRect()
  if (rowBox.top < listBox.top)
    list.scrollTop -= listBox.top - rowBox.top
  else if (rowBox.bottom > listBox.bottom)
    list.scrollTop += rowBox.bottom - listBox.bottom
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
