import { otoField, round3 } from "./otoFields"
import type { OtoMark, PageCommand } from "./protocol"

export type SheetKind = "file" | "alias" | "number" | "comment"

export type SheetColumn = {
  id: string
  header: string
  title: string
  kind: SheetKind
  point: number | null
  align: "left" | "right"
}

export type CellPos = { row: number; col: number }

export type CellRect = { r0: number; c0: number; r1: number; c1: number }

const maxMs = 86_400_000

export const sheetColumns: SheetColumn[] = [
  { id: "file", header: "ファイル", title: "WAV の名前", kind: "file", point: null, align: "left" },
  { id: "alias", header: "エイリアス", title: "oto.ini に書く名前。編集印の * はコピーされません", kind: "alias", point: null, align: "left" },
  { id: "offset", header: "オフセット", title: "ファイル先頭からのミリ秒。0 未満にはしない", kind: "number", point: 0, align: "right" },
  { id: "overlap", header: "オーバーラップ", title: "オフセットからのミリ秒。前にあるときは負の数", kind: "number", point: 1, align: "right" },
  { id: "preutter", header: "先行発声", title: "オフセットからのミリ秒。0 未満にはしない", kind: "number", point: 2, align: "right" },
  { id: "consonant", header: "子音部", title: "オフセットからのミリ秒。0 未満にはしない", kind: "number", point: 3, align: "right" },
  { id: "blank", header: "ブランク", title: "オフセットから終端までの時間を、負の数で書く", kind: "number", point: 4, align: "right" },
  { id: "comment", header: "メモ", title: "oto.ini には書き出さない", kind: "comment", point: null, align: "left" },
]

export function columnLetter(col: number): string {
  return String.fromCharCode("A".charCodeAt(0) + col)
}

export function cellAddress(pos: CellPos): string {
  return `${columnLetter(pos.col)}${pos.row + 1}`
}

export function normalizeRect(a: CellPos, b: CellPos): CellRect {
  return {
    r0: Math.min(a.row, b.row),
    c0: Math.min(a.col, b.col),
    r1: Math.max(a.row, b.row),
    c1: Math.max(a.col, b.col),
  }
}

export function rectContains(rect: CellRect, row: number, col: number): boolean {
  return row >= rect.r0 && row <= rect.r1 && col >= rect.c0 && col <= rect.c1
}

export function clampPos(pos: CellPos, rowCount: number): CellPos {
  const lastRow = Math.max(0, rowCount - 1)
  return {
    row: Math.min(Math.max(pos.row, 0), lastRow),
    col: Math.min(Math.max(pos.col, 0), sheetColumns.length - 1),
  }
}

export function moveCell(pos: CellPos, rowDelta: number, colDelta: number, rowCount: number): CellPos {
  return clampPos({ row: pos.row + rowDelta, col: pos.col + colDelta }, rowCount)
}

export function tabCell(pos: CellPos, backward: boolean, rowCount: number): CellPos {
  let row = pos.row
  let col = pos.col + (backward ? -1 : 1)
  if (col >= sheetColumns.length) {
    col = 0
    row += 1
  } else if (col < 0) {
    col = sheetColumns.length - 1
    row -= 1
  }
  if (row < 0 || row >= rowCount)
    return pos
  return { row, col }
}

export function edgeCell(pos: CellPos, rowDelta: number, colDelta: number, rowCount: number): CellPos {
  if (colDelta !== 0)
    return { row: pos.row, col: colDelta > 0 ? sheetColumns.length - 1 : 0 }
  if (rowDelta !== 0)
    return { row: rowDelta > 0 ? Math.max(0, rowCount - 1) : 0, col: pos.col }
  return pos
}

export function cellText(mark: OtoMark, col: number): string {
  const column = sheetColumns[col]
  if (!column)
    return ""
  if (column.kind === "file")
    return mark.file
  if (column.kind === "alias")
    return mark.name || mark.autoAlias
  if (column.kind === "comment")
    return mark.comment
  return formatMs(otoField(mark, column.point ?? 0))
}

export function cellStar(mark: OtoMark, col: number): boolean {
  const column = sheetColumns[col]
  if (!column)
    return false
  if (column.kind === "alias")
    return mark.name.length > 0 || mark.tuned.some(Boolean)
  if (column.kind === "number")
    return mark.tuned[column.point ?? 0] === true
  return false
}

export function copyText(rows: OtoMark[], rect: CellRect): string {
  const lines: string[] = []
  for (let row = rect.r0; row <= rect.r1; row++) {
    const mark = rows[row]
    if (!mark)
      break
    const cells: string[] = []
    for (let col = rect.c0; col <= rect.c1; col++)
      cells.push(cellText(mark, col))
    lines.push(cells.join("\t"))
  }
  return lines.join("\n")
}

export function parseGrid(text: string): string[][] {
  if (text.length === 0)
    return [[""]]
  let body = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n")
  if (body.endsWith("\n"))
    body = body.slice(0, -1)
  if (body.length === 0)
    return [[""]]
  return body.split("\n").map((line) => line.split("\t"))
}

export function isBlock(text: string): boolean {
  const trimmed = text.replace(/[\r\n]+$/, "")
  return trimmed.includes("\t") || trimmed.includes("\n")
}

export function pasteCommands(rows: OtoMark[], active: CellPos, selection: CellRect, text: string): PageCommand[] {
  const grid = parseGrid(text)
  const one = grid.length === 1 && (grid[0]?.length ?? 0) === 1
  const wide = selection.r0 !== selection.r1 || selection.c0 !== selection.c1
  const drafts = new Map<number, RowDraft>()
  if (one && wide) {
    const value = grid[0]?.[0] ?? ""
    for (let row = selection.r0; row <= selection.r1; row++) {
      for (let col = selection.c0; col <= selection.c1; col++)
        writeCell(drafts, rows, row, col, value)
    }
  } else {
    for (let i = 0; i < grid.length; i++) {
      const line = grid[i] ?? []
      const row = active.row + i
      if (row < 0 || row >= rows.length)
        break
      const origin = sheetColumns[active.col]
      const whole = origin?.kind === "number" ? wholeRow(line) : null
      if (whole) {
        const draft = draftOf(drafts, rows, row)
        if (draft)
          draft.fields = whole
        continue
      }
      for (let j = 0; j < line.length; j++)
        writeCell(drafts, rows, row, active.col + j, line[j] ?? "")
    }
  }
  return commandsOf(drafts)
}

export function commitCommands(rows: OtoMark[], pos: CellPos, text: string): PageCommand[] {
  return pasteCommands(rows, pos, normalizeRect(pos, pos), text)
}

type RowDraft = {
  mark: OtoMark
  fields: number[] | null
  alias: string | null
  comment: string | null
}

function writeCell(drafts: Map<number, RowDraft>, rows: OtoMark[], row: number, col: number, text: string): void {
  const column = sheetColumns[col]
  const draft = draftOf(drafts, rows, row)
  if (!column || !draft || column.kind === "file")
    return
  if (column.kind === "number") {
    const parsed = tryParseMs(text)
    if (parsed == null)
      return
    const fields = draft.fields ?? fieldValues(draft.mark)
    fields[column.point ?? 0] = clampField(column.point ?? 0, parsed)
    draft.fields = fields
    return
  }
  if (column.kind === "alias") {
    const cleaned = cleanAlias(text)
    if (cleaned != null)
      draft.alias = cleaned
    return
  }
  draft.comment = cleanComment(text) ?? ""
}

function draftOf(drafts: Map<number, RowDraft>, rows: OtoMark[], row: number): RowDraft | null {
  const mark = rows[row]
  if (!mark)
    return null
  const existing = drafts.get(row)
  if (existing)
    return existing
  const draft: RowDraft = { mark, fields: null, alias: null, comment: null }
  drafts.set(row, draft)
  return draft
}

function commandsOf(drafts: Map<number, RowDraft>): PageCommand[] {
  const commands: PageCommand[] = []
  for (const draft of drafts.values()) {
    const mark = draft.mark
    if (draft.fields && differs(fieldValues(mark), draft.fields)) {
      commands.push({
        type: "oto",
        op: "fields",
        file: mark.file,
        index: mark.index,
        alias: mark.autoAlias,
        offset: draft.fields[0] ?? 0,
        overlap: draft.fields[1] ?? 0,
        preutter: draft.fields[2] ?? 0,
        consonant: draft.fields[3] ?? 0,
        blank: draft.fields[4] ?? 0,
      })
    }
    const shown = mark.name || mark.autoAlias
    if (draft.alias != null && draft.alias !== shown) {
      commands.push({
        type: "oto",
        op: "aliasName",
        file: mark.file,
        index: mark.index,
        alias: mark.autoAlias,
        name: draft.alias,
      })
    }
    if (draft.comment != null && draft.comment !== mark.comment) {
      commands.push({
        type: "oto",
        op: "comment",
        file: mark.file,
        index: mark.index,
        alias: mark.autoAlias,
        comment: draft.comment,
      })
    }
  }
  return commands
}

function fieldValues(mark: OtoMark): number[] {
  return [0, 1, 2, 3, 4].map((point) => otoField(mark, point))
}

function differs(current: number[], next: number[]): boolean {
  return current.some((value, index) => Math.abs(value - (next[index] ?? value)) >= 0.0005)
}

function formatMs(ms: number): string {
  const rounded = round3(ms)
  return (Number.isFinite(rounded) ? rounded : 0).toFixed(3)
}

function clampField(point: number, ms: number): number {
  const rounded = round3(ms)
  if (!Number.isFinite(rounded))
    return 0
  if ((point === 0 || point === 2 || point === 3) && rounded < 0)
    return 0
  return rounded
}

function tryParseMs(text: string): number | null {
  if (text.trim().length === 0)
    return null
  let source = text.trim()
  if (source.includes(",") && source.includes("."))
    source = source.replace(/,/g, "")
  else
    source = source.replace(/,/g, ".")
  const value = Number(source)
  if (!Number.isFinite(value) || Math.abs(value) > maxMs)
    return null
  return value
}

function cleanAlias(text: string): string | null {
  const source = text.replaceAll("*", "").trim()
  if (source.length === 0 || source.length > 80)
    return null
  if (/[\r\n\t=,]/u.test(source))
    return null
  return source
}

function cleanComment(text: string): string | null {
  const source = text.replace(/[\r\n\t]/gu, " ").trim()
  if (source.length === 0)
    return null
  return source.length > 200 ? source.slice(0, 200) : source
}

function wholeRow(cells: string[]): number[] | null {
  const numbers: number[] = []
  let sawText = false
  for (const cell of cells) {
    if (cell.trim().length === 0)
      continue
    const parsed = tryParseMs(cell)
    if (parsed != null) {
      numbers.push(parsed)
      continue
    }
    if (numbers.length === 0) {
      sawText = true
      continue
    }
    break
  }
  if (!sawText || numbers.length !== 5)
    return null
  return numbers.map((value, index) => clampField(index, value))
}
