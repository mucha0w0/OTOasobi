import { sameName } from "./filePick"
import { aliasText } from "./otoSheet"
import type { OtoMark } from "./protocol"

export type AliasRef = {
  file: string
  index: number
  alias?: string
  autoAlias: string
  mode?: string
}

export function aliasId(row: AliasRef): string {
  return `${row.file}\0${row.index}\0${row.mode ?? ""}`
}

export function findAlias(rows: OtoMark[], id: string): OtoMark | undefined {
  return rows.find((row) => aliasId(row) === id)
}

export function selectedAliasIds(rows: OtoMark[], files: string[]): string[] {
  return rows.filter((row) => files.some((file) => sameName(file, row.file))).map(aliasId)
}

export function currentAliasId(rows: OtoMark[], file: string, focus: number, mode?: string): string {
  if (focus < 0 || !file)
    return ""
  const hits = rows.filter((row) => sameName(row.file, file) && row.index === focus)
  const matched = mode ? hits.find((row) => row.mode === mode) : hits[0]
  return matched ? aliasId(matched) : ""
}

export function markIndexForAlias(row: AliasRef, marks: OtoMark[], mode?: string): number {
  if (!row.mode || row.mode === mode) {
    if (marks.some((mark) => mark.index === row.index))
      return row.index
  }
  const name = (row.alias ?? "").replace(/ \*$/u, "")
  const hit = marks.find((mark) => mark.autoAlias === row.autoAlias || (name.length > 0 && aliasText(mark) === name))
  return hit?.index ?? -1
}
