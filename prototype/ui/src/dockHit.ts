import type { DockEdge } from "./layout"

export type ScreenBox = { x: number; y: number; width: number; height: number }

export type HitLeaf = { id: string; rect: ScreenBox }

export type HitRegion = { ids: string[]; rect: ScreenBox }

export type DropAim = {
  edge: DockEdge | null
  targetIds: string[]
  highlight: ScreenBox | null
  arms: ScreenBox | null
  hot: boolean
}

const armSize = 28
const armGap = 6
const sideBand = 44
const endBand = 16

const edges = ["left", "right", "top", "bottom"] as const

export function armBox(target: ScreenBox, edge: DockEdge): ScreenBox {
  const cx = target.x + target.width / 2
  const cy = target.y + target.height / 2
  if (edge === "left")
    return { x: cx - armGap - armSize, y: cy - armSize / 2, width: armSize, height: armSize }
  if (edge === "right")
    return { x: cx + armGap, y: cy - armSize / 2, width: armSize, height: armSize }
  if (edge === "top")
    return { x: cx - armSize / 2, y: cy - armGap - armSize, width: armSize, height: armSize }
  return { x: cx - armSize / 2, y: cy + armGap, width: armSize, height: armSize }
}

// 左右の予告は、半分の箱を対象の幅に戻してから端へ寄せる。右は対象の右端が基準。
export function previewBox(half: ScreenBox, edge: "left" | "right", fraction: number): ScreenBox {
  const host = half.width * 2
  const width = host * fraction
  const x = edge === "left" ? half.x : half.x + half.width - width
  return { ...half, x, width }
}

export function halfBox(target: ScreenBox, edge: DockEdge): ScreenBox {
  if (edge === "left")
    return { x: target.x, y: target.y, width: target.width / 2, height: target.height }
  if (edge === "right")
    return { x: target.x + target.width / 2, y: target.y, width: target.width / 2, height: target.height }
  if (edge === "top")
    return { x: target.x, y: target.y, width: target.width, height: target.height / 2 }
  return { x: target.x, y: target.y + target.height / 2, width: target.width, height: target.height / 2 }
}

export function hitDrop(leaves: HitLeaf[], regions: HitRegion[], draggedId: string, x: number, y: number): DropAim | null {
  const leaf = leaves.find((item) => item.id !== draggedId && contains(item.rect, x, y))
  if (leaf) {
    const edge = hitArm(leaf.rect, x, y)
    if (edge)
      return { edge, targetIds: [leaf.id], highlight: halfBox(leaf.rect, edge), arms: leaf.rect, hot: true }
  }

  let best: { region: HitRegion; edge: DockEdge; distance: number } | null = null
  for (const region of regions) {
    if (!region.ids.some((id) => id !== draggedId))
      continue
    const found = edgeOf(region.rect, x, y)
    if (!found)
      continue
    const area = region.rect.width * region.rect.height
    const nearer = !best || found.distance < best.distance - 0.5
    const sameAndLarger = best != null && Math.abs(found.distance - best.distance) <= 0.5 && area > best.region.rect.width * best.region.rect.height + 0.5
    if (!nearer && !sameAndLarger)
      continue
    best = { region, edge: found.edge, distance: found.distance }
  }
  if (best) {
    const targetIds = best.region.ids.filter((id) => id !== draggedId)
    const onThatPane = leaf != null && targetIds.length === 1 && targetIds[0] === leaf.id
    return {
      edge: best.edge,
      targetIds,
      highlight: halfBox(best.region.rect, best.edge),
      arms: onThatPane ? leaf.rect : null,
      hot: false,
    }
  }
  if (leaf)
    return { edge: null, targetIds: [], highlight: null, arms: leaf.rect, hot: false }
  return null
}

function hitArm(target: ScreenBox, x: number, y: number): DockEdge | null {
  let best: DockEdge | null = null
  for (const edge of edges) {
    if (contains(armBox(target, edge), x, y))
      best = edge
  }
  return best
}

function edgeOf(rect: ScreenBox, x: number, y: number): { edge: DockEdge; distance: number } | null {
  if (!contains(rect, x, y))
    return null
  let distance = x - rect.x
  let edge: DockEdge = "left"
  const right = rect.x + rect.width - x
  const top = y - rect.y
  const bottom = rect.y + rect.height - y
  if (right < distance) {
    distance = right
    edge = "right"
  }
  if (top < distance) {
    distance = top
    edge = "top"
  }
  if (bottom < distance) {
    distance = bottom
    edge = "bottom"
  }
  const band = edge === "left" || edge === "right" ? sideBand : endBand
  return distance <= band ? { edge, distance } : null
}

function contains(box: ScreenBox, x: number, y: number) {
  return x >= box.x && y >= box.y && x <= box.x + box.width && y <= box.y + box.height
}
