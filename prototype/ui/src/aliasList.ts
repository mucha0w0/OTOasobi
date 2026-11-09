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

export function aliasStepIndex(rows: OtoMark[], file: string, focus: number, mode?: string): number {
  const id = currentAliasId(rows, file, focus, mode)
  if (id) {
    const at = rows.findIndex((row) => aliasId(row) === id)
    if (at >= 0)
      return at
  }
  return rows.findIndex((row) => sameName(row.file, file))
}

export function aliasPanelMode(mode: string | undefined): "mono" | "renzoku" | "cvvc" | null {
  if (mode === "mono" || mode === "renzoku" || mode === "cvvc")
    return mode
  return null
}

export function pendingAliasReady(pending: { file: string; mode?: string }, file: string, mode: string): boolean {
  if (!sameName(pending.file, file))
    return false
  const want = aliasPanelMode(pending.mode)
  return want == null || want === mode
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
