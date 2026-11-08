import type { LayoutNode, PaneBox } from "./layout"

/** 画面に出ていた下限。領域が 2 枚分より狭いときは 8%。広いときは 160px。 */
export const minPane = 160

// 押し始めの座標にほかの境目を固定したまま、dragged だけを proposed へ進める。
// 固定した境目が動く位置は採用しない。途中にその区間があっても飛び越さない。
// 縮む面が下限を下回る位置も採用しない。
export function moveSplitter(
  root: LayoutNode,
  dragged: number[],
  proposed: number,
  frozen: ReadonlyMap<string, number>,
  width: number,
  height: number,
  floors?: ReadonlyMap<string, PaneBox>,
): LayoutNode {
  if (width < 1 || height < 1 || !Number.isFinite(proposed))
    return root
  const draggedKey = pathKey(dragged)
  if (!frozen.has(draggedKey))
    return root
  const tree = cloneNode(root)
  if (!branchAt(tree, dragged))
    return root
  const current = splitterAt(tree, dragged, width, height)
  if (current == null || Math.abs(proposed - current) < 0.5)
    return root

  const floorMap = floors ?? measureFloors(tree, width, height)
  const dir = Math.sign(proposed - current)
  let accepted = current
  const steps = Math.min(4000, Math.ceil(Math.abs(proposed - current)))
  for (let i = 1; i <= steps; i++) {
    const next = i === steps ? proposed : current + dir * i
    placeSplitter(tree, [], draggedKey, next, frozen, 0, 0, width, height)
    const landed = splitterAt(tree, dragged, width, height)
    if (
      !splittersHeld(tree, [], draggedKey, frozen, 0, 0, width, height)
      || !panesAboveFloors(tree, [], floorMap, width, height)
      || landed == null
      || (landed - accepted) * dir < 0.25
    ) {
      placeSplitter(tree, [], draggedKey, accepted, frozen, 0, 0, width, height)
      break
    }
    accepted = landed
    if (Math.abs(landed - proposed) < 0.5)
      break
  }

  if (Math.abs(accepted - current) < 0.5)
    return root
  return tree
}

export function measureSplitters(root: LayoutNode, width: number, height: number): Map<string, number> {
  const found = new Map<string, number>()
  if (width < 1 || height < 1)
    return found
  walkSplitters(root, [], 0, 0, width, height, found)
  return found
}

export function measureFloors(root: LayoutNode, width: number, height: number): Map<string, PaneBox> {
  const floors = new Map<string, PaneBox>()
  if (width < 1 || height < 1)
    return floors
  collectFloors(root, [], width, height, width, height, floors)
  return floors
}

export function pathKey(path: number[]): string {
  return path.join(".")
}

function walkSplitters(
  node: LayoutNode,
  path: number[],
  x: number,
  y: number,
  width: number,
  height: number,
  found: Map<string, number>,
) {
  const span = trySpan(node, width, height)
  if (!span || "id" in node)
    return
  found.set(pathKey(path), (span.horizontal ? x : y) + span.size)
  if (span.horizontal) {
    walkSplitters(node.first, [...path, 0], x, y, span.size, height, found)
    walkSplitters(node.second, [...path, 1], x + span.size, y, Math.max(0, width - span.size), height, found)
    return
  }
  walkSplitters(node.first, [...path, 0], x, y, width, span.size, found)
  walkSplitters(node.second, [...path, 1], x, y + span.size, width, Math.max(0, height - span.size), found)
}

function placeSplitter(
  node: LayoutNode,
  path: number[],
  draggedKey: string,
  dragAt: number,
  frozen: ReadonlyMap<string, number>,
  x: number,
  y: number,
  width: number,
  height: number,
) {
  const span = trySpan(node, width, height)
  if (!span || "id" in node)
    return
  const key = pathKey(path)
  const at = key === draggedKey
    ? dragAt
    : frozen.get(key) ?? (span.horizontal ? x : y) + span.span * node.ratio
  node.ratio = clampRatio((at - (span.horizontal ? x : y)) / span.span, span.span)
  const size = span.span * node.ratio
  if (span.horizontal) {
    placeSplitter(node.first, [...path, 0], draggedKey, dragAt, frozen, x, y, size, height)
    placeSplitter(node.second, [...path, 1], draggedKey, dragAt, frozen, x + size, y, Math.max(0, width - size), height)
    return
  }
  placeSplitter(node.first, [...path, 0], draggedKey, dragAt, frozen, x, y, width, size)
  placeSplitter(node.second, [...path, 1], draggedKey, dragAt, frozen, x, y + size, width, Math.max(0, height - size))
}

function splittersHeld(
  node: LayoutNode,
  path: number[],
  draggedKey: string,
  frozen: ReadonlyMap<string, number>,
  x: number,
  y: number,
  width: number,
  height: number,
): boolean {
  const span = trySpan(node, width, height)
  if (!span || "id" in node)
    return true
  const key = pathKey(path)
  if (key !== draggedKey) {
    const at = (span.horizontal ? x : y) + span.size
    const expected = frozen.get(key)
    if (expected == null || Math.abs(at - expected) > 0.5)
      return false
  }
  if (span.horizontal) {
    return splittersHeld(node.first, [...path, 0], draggedKey, frozen, x, y, span.size, height)
      && splittersHeld(node.second, [...path, 1], draggedKey, frozen, x + span.size, y, Math.max(0, width - span.size), height)
  }
  return splittersHeld(node.first, [...path, 0], draggedKey, frozen, x, y, width, span.size)
    && splittersHeld(node.second, [...path, 1], draggedKey, frozen, x, y + span.size, width, Math.max(0, height - span.size))
}

function collectFloors(
  node: LayoutNode,
  path: number[],
  width: number,
  height: number,
  widthSpan: number,
  heightSpan: number,
  floors: Map<string, PaneBox>,
) {
  if ("id" in node) {
    floors.set(pathKey(path), { width: shrinkFloor(width, widthSpan), height: shrinkFloor(height, heightSpan) })
    return
  }
  const span = trySpan(node, width, height)
  if (!span)
    return
  if (span.horizontal) {
    collectFloors(node.first, [...path, 0], span.size, height, span.span, heightSpan, floors)
    collectFloors(node.second, [...path, 1], Math.max(0, width - span.size), height, span.span, heightSpan, floors)
    return
  }
  collectFloors(node.first, [...path, 0], width, span.size, widthSpan, span.span, floors)
  collectFloors(node.second, [...path, 1], width, Math.max(0, height - span.size), widthSpan, span.span, floors)
}

function panesAboveFloors(
  node: LayoutNode,
  path: number[],
  floors: ReadonlyMap<string, PaneBox>,
  width: number,
  height: number,
): boolean {
  if ("id" in node) {
    const floor = floors.get(pathKey(path))
    if (!floor)
      return false
    return width + 0.5 >= floor.width && height + 0.5 >= floor.height
  }
  const span = trySpan(node, width, height)
  if (!span)
    return false
  if (span.horizontal) {
    return panesAboveFloors(node.first, [...path, 0], floors, span.size, height)
      && panesAboveFloors(node.second, [...path, 1], floors, Math.max(0, width - span.size), height)
  }
  return panesAboveFloors(node.first, [...path, 0], floors, width, span.size)
    && panesAboveFloors(node.second, [...path, 1], floors, width, Math.max(0, height - span.size))
}

function shrinkFloor(current: number, axisSpan: number): number {
  const limit = axisSpan <= minPane * 2 ? Math.max(1, axisSpan * 0.08) : minPane
  return Math.min(Math.max(0, current), limit)
}

function splitterAt(root: LayoutNode, dragged: number[], width: number, height: number): number | null {
  const found = { at: 0, ok: false }
  findSplitter(root, [], pathKey(dragged), 0, 0, width, height, found)
  return found.ok ? found.at : null
}

function findSplitter(
  node: LayoutNode,
  path: number[],
  draggedKey: string,
  x: number,
  y: number,
  width: number,
  height: number,
  found: { at: number; ok: boolean },
): boolean {
  const span = trySpan(node, width, height)
  if (!span || "id" in node)
    return false
  if (pathKey(path) === draggedKey) {
    found.at = (span.horizontal ? x : y) + span.size
    found.ok = true
    return true
  }
  if (span.horizontal) {
    return findSplitter(node.first, [...path, 0], draggedKey, x, y, span.size, height, found)
      || findSplitter(node.second, [...path, 1], draggedKey, x + span.size, y, Math.max(0, width - span.size), height, found)
  }
  return findSplitter(node.first, [...path, 0], draggedKey, x, y, width, span.size, found)
    || findSplitter(node.second, [...path, 1], draggedKey, x, y + span.size, width, Math.max(0, height - span.size), found)
}

function trySpan(node: LayoutNode, width: number, height: number): { horizontal: boolean; span: number; size: number } | null {
  if ("id" in node)
    return null
  const horizontal = node.axis === "h"
  const span = horizontal ? width : height
  if (span < 1)
    return null
  return { horizontal, span, size: span * clampRatio(node.ratio, span) }
}

function clampRatio(ratio: number, total: number): number {
  if (!Number.isFinite(ratio))
    ratio = 0.5
  if (total <= 2)
    return Math.min(0.92, Math.max(0.08, ratio))
  const edge = Math.min(0.49, 1 / total)
  return Math.min(1 - edge, Math.max(edge, ratio))
}

function branchAt(node: LayoutNode, path: number[]): boolean {
  let current = node
  for (const step of path) {
    if ("id" in current)
      return false
    current = step === 0 ? current.first : current.second
  }
  return !("id" in current)
}

function cloneNode(node: LayoutNode): LayoutNode {
  if ("id" in node)
    return { id: node.id }
  return { axis: node.axis, ratio: node.ratio, first: cloneNode(node.first), second: cloneNode(node.second) }
}
