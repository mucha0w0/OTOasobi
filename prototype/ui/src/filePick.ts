export type FilePick = {
  selected: string[]
  active: string
  anchor: string
}

export const emptyFilePick: FilePick = { selected: [], active: "", anchor: "" }

export function sameName(left: string, right: string) {
  return left.localeCompare(right, undefined, { sensitivity: "accent" }) === 0
}

function findIndex(names: string[], name: string) {
  return names.findIndex((item) => sameName(item, name))
}

function inOrder(names: string[], picked: string[]) {
  return names.filter((name) => picked.some((item) => sameName(item, name)))
}

function nearest(names: string[], selected: string[], from: number) {
  let best = selected[0] ?? ""
  let distance = Number.POSITIVE_INFINITY
  names.forEach((name, index) => {
    if (!selected.some((item) => sameName(item, name)))
      return
    const gap = Math.abs(index - from)
    if (gap < distance) {
      distance = gap
      best = name
    }
  })
  return best
}

export function applyFileClick(names: string[], pick: FilePick, name: string, shift: boolean, ctrl: boolean): FilePick {
  const index = findIndex(names, name)
  if (index < 0)
    return pick
  const clicked = names[index] ?? name
  const anchorIndex = findIndex(names, pick.anchor)
  if (shift && anchorIndex >= 0) {
    const lo = Math.min(anchorIndex, index)
    const hi = Math.max(anchorIndex, index)
    const range = names.slice(lo, hi + 1)
    const selected = ctrl ? inOrder(names, [...pick.selected, ...range]) : range
    return { selected, active: clicked, anchor: names[anchorIndex] ?? pick.anchor }
  }
  if (ctrl) {
    const has = pick.selected.some((item) => sameName(item, clicked))
    const selected = inOrder(names, has ? pick.selected.filter((item) => !sameName(item, clicked)) : [...pick.selected, clicked])
    if (selected.length === 0)
      return { selected: [clicked], active: clicked, anchor: clicked }
    let active = pick.active
    if (!has)
      active = clicked
    else if (sameName(active, clicked) || !selected.some((item) => sameName(item, active)))
      active = nearest(names, selected, index)
    return { selected, active, anchor: clicked }
  }
  return { selected: [clicked], active: clicked, anchor: clicked }
}

export function pruneFilePick(names: string[], pick: FilePick): FilePick {
  let selected = inOrder(names, pick.selected)
  const activeIndex = findIndex(names, pick.active)
  if (selected.length === 0 && activeIndex >= 0)
    selected = [names[activeIndex] ?? pick.active]
  const anchorIndex = findIndex(names, pick.anchor)
  return {
    selected,
    active: activeIndex >= 0 ? (names[activeIndex] ?? pick.active) : pick.active,
    anchor: anchorIndex >= 0 ? (names[anchorIndex] ?? pick.anchor) : (selected[0] ?? ""),
  }
}
