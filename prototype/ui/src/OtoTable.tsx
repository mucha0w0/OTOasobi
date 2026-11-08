import { useEffect, useRef, useState, type KeyboardEvent } from "react"
import {
  cellStar,
  cellText,
  clampPos,
  commitCommands,
  copyText,
  edgeCell,
  isBlock,
  moveCell,
  normalizeRect,
  pasteCommands,
  rectContains,
  sheetColumns,
  tabCell,
  type CellPos,
} from "./otoSheet"
import type { OtoMark, PageCommand } from "./protocol"

export function OtoTable({
  rows,
  send,
}: {
  rows: OtoMark[]
  send: (command: PageCommand) => void
}) {
  const [active, setActive] = useState<CellPos>({ row: 0, col: 0 })
  const [extent, setExtent] = useState<CellPos>({ row: 0, col: 0 })
  const [draft, setDraft] = useState("")
  const [editAt, setEditAt] = useState<"cell" | null>(null)
  const rowsRef = useRef(rows)
  const activeRef = useRef(active)
  const extentRef = useRef(extent)
  const draftRef = useRef(draft)
  const editRef = useRef(editAt)
  const gridRef = useRef<HTMLDivElement>(null)
  const editorRef = useRef<HTMLInputElement>(null)
  const dragRef = useRef<"cell" | "col" | null>(null)
  const scrollRef = useRef(false)
  const skipCommit = useRef(false)
  const caretRef = useRef(false)
  rowsRef.current = rows
  activeRef.current = active
  extentRef.current = extent
  draftRef.current = draft
  editRef.current = editAt

  useEffect(() => {
    setActive((pos) => clampPos(pos, rows.length))
    setExtent((pos) => clampPos(pos, rows.length))
  }, [rows.length])

  useEffect(() => {
    if (!scrollRef.current)
      return
    scrollRef.current = false
    gridRef.current?.querySelector<HTMLElement>("[data-active='true']")?.scrollIntoView({ block: "nearest", inline: "nearest" })
  }, [active])

  useEffect(() => {
    const editor = editorRef.current
    if (!editor)
      return
    editor.focus()
    if (caretRef.current)
      editor.setSelectionRange(editor.value.length, editor.value.length)
    else
      editor.select()
  }, [editAt])

  useEffect(() => {
    const move = (event: PointerEvent) => {
      const kind = dragRef.current
      if (!kind)
        return
      const hit = document.elementFromPoint(event.clientX, event.clientY)?.closest<HTMLElement>("[data-row][data-col]")
      if (!hit)
        return
      const row = Number(hit.dataset.row)
      const col = Number(hit.dataset.col)
      if (!Number.isInteger(row) || !Number.isInteger(col))
        return
      if (kind === "col")
        setExtent({ row: rowsRef.current.length - 1, col })
      else
        setExtent({ row, col })
    }
    const up = () => {
      dragRef.current = null
    }
    window.addEventListener("pointermove", move)
    window.addEventListener("pointerup", up)
    return () => {
      window.removeEventListener("pointermove", move)
      window.removeEventListener("pointerup", up)
    }
  }, [])

  if (rows.length === 0)
    return null

  const rect = normalizeRect(active, extent)
  const current = rows[active.row]
  const shown = current ? cellText(current, active.col) : ""

  function focusGrid() {
    gridRef.current?.focus({ preventScroll: true })
  }

  function sendText(text: string, origin = activeRef.current, selection = normalizeRect(activeRef.current, extentRef.current)) {
    for (const command of pasteCommands(rowsRef.current, origin, selection, text))
      send(command)
  }

  function commitEdit() {
    if (!editRef.current)
      return
    const pos = activeRef.current
    const text = draftRef.current
    editRef.current = null
    setEditAt(null)
    for (const command of commitCommands(rowsRef.current, pos, text))
      send(command)
  }

  function beginEdit(initial: string, caret: boolean) {
    const col = sheetColumns[activeRef.current.col]
    if (!col || col.kind === "file")
      return
    caretRef.current = caret
    draftRef.current = initial
    setDraft(initial)
    setEditAt("cell")
  }

  function moveActive(next: CellPos, extend: boolean) {
    scrollRef.current = true
    if (extend)
      setExtent(next)
    else {
      setActive(next)
      setExtent(next)
    }
  }

  function selectCell(pos: CellPos, extend: boolean) {
    if (editRef.current)
      commitEdit()
    if (extend) {
      extentRef.current = pos
      setExtent(pos)
    } else {
      activeRef.current = pos
      extentRef.current = pos
      setActive(pos)
      setExtent(pos)
    }
    focusGrid()
  }

  return (
    <div
      ref={gridRef}
      tabIndex={0}
      aria-label="oto数値表"
      className="sheet-wrap"
      onCopy={(event) => {
        if (editRef.current)
          return
        event.preventDefault()
        event.clipboardData.setData("text/plain", copyText(rows, rect))
      }}
      onCut={(event) => {
        if (editRef.current)
          return
        event.preventDefault()
        event.clipboardData.setData("text/plain", copyText(rows, rect))
      }}
      onPaste={(event) => {
        if (editRef.current)
          return
        event.preventDefault()
        sendText(event.clipboardData.getData("text/plain"))
      }}
      onKeyDown={(event) => {
        if (event.target instanceof HTMLInputElement)
          return
        const meta = event.ctrlKey || event.metaKey
        if (meta && event.key.toLowerCase() === "a") {
          event.preventDefault()
          setActive({ row: 0, col: 0 })
          setExtent({ row: rows.length - 1, col: sheetColumns.length - 1 })
          return
        }
        if (meta && (event.key.toLowerCase() === "c" || event.key.toLowerCase() === "v" || event.key.toLowerCase() === "x"))
          return
        if (event.key === "F2") {
          event.preventDefault()
          beginEdit(shown, false)
          return
        }
        if (event.key === "Escape") {
          setExtent(active)
          return
        }
        const arrow = event.key === "ArrowUp" ? [-1, 0] : event.key === "ArrowDown" ? [1, 0] : event.key === "ArrowLeft" ? [0, -1] : event.key === "ArrowRight" ? [0, 1] : null
        if (arrow) {
          event.preventDefault()
          const [rowDelta, colDelta] = arrow
          const next = meta ? edgeCell(event.shiftKey ? extent : active, rowDelta ?? 0, colDelta ?? 0, rows.length) : moveCell(event.shiftKey ? extent : active, rowDelta ?? 0, colDelta ?? 0, rows.length)
          moveActive(next, event.shiftKey)
          return
        }
        if (event.key === "Enter") {
          event.preventDefault()
          moveActive(moveCell(active, event.shiftKey ? -1 : 1, 0, rows.length), false)
          return
        }
        if (event.key === "Tab") {
          event.preventDefault()
          moveActive(tabCell(active, event.shiftKey, rows.length), false)
          return
        }
        if (event.key.length === 1 && !meta && !event.altKey) {
          event.preventDefault()
          beginEdit(event.key, true)
        }
      }}
    >
      <div className="sheet-scroll">
        <table className="sheet">
          <colgroup>
            <col className="sheet-col-file" />
            <col className="sheet-col-alias" />
            <col className="sheet-col-num" />
            <col className="sheet-col-num" />
            <col className="sheet-col-num" />
            <col className="sheet-col-num" />
            <col className="sheet-col-num" />
            <col />
          </colgroup>
          <thead>
            <tr>
              {sheetColumns.map((item, col) => (
                <th
                  key={item.id}
                  data-row={0}
                  data-col={col}
                  title={item.title}
                  className={rect.c0 <= col && col <= rect.c1 ? "is-hot" : ""}
                  onPointerDown={(event) => {
                    if (event.button !== 0)
                      return
                    selectCell({ row: 0, col }, false)
                    setExtent({ row: rows.length - 1, col })
                    dragRef.current = "col"
                  }}
                >
                  {item.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((mark, row) => (
              <tr key={`${mark.file}:${mark.mode ?? ""}:${mark.index}:${mark.autoAlias}`}>
                {sheetColumns.map((item, col) => {
                  const here = active.row === row && active.col === col
                  const inside = rectContains(rect, row, col)
                  const edges = [
                    inside && row === rect.r0 ? "inset 0 2px 0 var(--accent)" : "",
                    inside && row === rect.r1 ? "inset 0 -2px 0 var(--accent)" : "",
                    inside && col === rect.c0 ? "inset 2px 0 0 var(--accent)" : "",
                    inside && col === rect.c1 ? "inset -2px 0 0 var(--accent)" : "",
                  ].filter(Boolean).join(", ")
                  const text = cellText(mark, col)
                  const star = cellStar(mark, col)
                  return (
                    <td
                      key={item.id}
                      data-row={row}
                      data-col={col}
                      data-active={here ? "true" : "false"}
                      className={`${item.align === "right" ? "is-num" : ""} ${star ? "is-tuned" : ""} ${inside && !here ? "is-selected" : ""} ${here ? "is-active" : ""}`}
                      style={edges ? { boxShadow: edges } : undefined}
                      title={item.kind === "file" ? mark.file : undefined}
                      onPointerDown={(event) => {
                        if (event.button !== 0)
                          return
                        selectCell({ row, col }, event.shiftKey)
                        dragRef.current = event.shiftKey ? null : "cell"
                      }}
                      onDoubleClick={() => {
                        if (here)
                          beginEdit(text, false)
                      }}
                    >
                      {here && editAt === "cell" ? (
                        <input
                          ref={editorRef}
                          className="sheet-editor"
                          aria-label={item.header}
                          spellCheck={false}
                          autoComplete="off"
                          value={draft}
                          onPointerDown={(event) => event.stopPropagation()}
                          onChange={(event) => {
                            draftRef.current = event.target.value
                            setDraft(event.target.value)
                          }}
                          onBlur={() => {
                            if (skipCommit.current) {
                              skipCommit.current = false
                              return
                            }
                            commitEdit()
                          }}
                          onPaste={(event) => {
                            const pasted = event.clipboardData.getData("text/plain")
                            if (!isBlock(pasted))
                              return
                            event.preventDefault()
                            skipCommit.current = true
                            editRef.current = null
                            setEditAt(null)
                            sendText(pasted, { row, col }, normalizeRect({ row, col }, { row, col }))
                            focusGrid()
                          }}
                          onKeyDown={(event) => onEditorKey(event, commitEdit, () => {
                            skipCommit.current = true
                            editRef.current = null
                            setEditAt(null)
                            focusGrid()
                          }, (next) => {
                            skipCommit.current = true
                            commitEdit()
                            moveActive(next, false)
                            focusGrid()
                          }, { row, col }, rows.length)}
                        />
                      ) : (
                        <span className="sheet-text">
                          {star && item.align === "right" && <i className="sheet-star">*</i>}
                          <span>{text}</span>
                          {star && item.align === "left" && <i className="sheet-star">*</i>}
                        </span>
                      )}
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function onEditorKey(
  event: KeyboardEvent<HTMLInputElement>,
  commit: () => void,
  cancel: () => void,
  leave: (next: CellPos) => void,
  pos: CellPos,
  rowCount: number,
) {
  if (event.nativeEvent.isComposing || event.key === "Process")
    return
  if (event.key === "Escape") {
    event.preventDefault()
    cancel()
    return
  }
  if (event.key === "Enter") {
    event.preventDefault()
    leave(moveCell(pos, event.shiftKey ? -1 : 1, 0, rowCount))
    return
  }
  if (event.key === "Tab") {
    event.preventDefault()
    leave(tabCell(pos, event.shiftKey, rowCount))
    return
  }
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "enter") {
    event.preventDefault()
    commit()
  }
}
