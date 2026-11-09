import { minPane } from "./splitter"

export type LayoutNode =
  | { id: string }
  | { axis: "h" | "v"; ratio: number; first: LayoutNode; second: LayoutNode }

export type DockSpan = { width: number; height: number }

export type PaneBox = { width: number; height: number }

export const paneTitles: Record<string, string> = {
  file: "ファイルリスト",
  wave: "音素推定",
  spec: "スペクトログラム",
  loud: "音量",
  otoBand: "oto設定",
  source: "エフェクター",
  oto: "oto推定設定",
  otoParams: "oto数値表",
  otoIni: "oto.ini生成",
  frq: ".frq生成",
  frqEdit: "frqエディター",
  bank: "音源の各種設定",
  analyze: "音素解析",
  recList: "録音リスト",
  recScript: "録音スクリプト",
  rec: "録音コントローラ",
  guide: "ガイドBGM設定",
  mic: "マイクモニター",
}

// 表示メニューのフォルダ。同じ id は複数に置いてよく、開閉は id で揃う。
export const viewFolders: { title: string; ids: string[] }[] = [
  { title: "録音", ids: ["file", "recList", "recScript", "rec", "guide", "mic"] },
  { title: "原音設定の推定", ids: ["file", "analyze", "source", "wave", "spec", "loud", "oto"] },
  { title: "原音設定", ids: ["file", "wave", "spec", "loud", "otoBand", "otoParams", "otoIni"] },
  { title: "frq", ids: ["file", "frq", "frqEdit"] },
  { title: "パッケージ化", ids: ["bank"] },
]

const paneIds = Object.keys(paneTitles)

export function createDefault(): LayoutNode {
  return h(
    0.182,
    leaf("file"),
    h(
      0.746,
      v(0.3, leaf("wave"), v(0.457, leaf("spec"), v(0.368, leaf("loud"), leaf("otoBand")))),
      v(0.78, leaf("oto"), leaf("otoIni")),
    ),
  )
}

export function createRecording(): LayoutNode {
  return h(
    0.22,
    leaf("recList"),
    v(
      0.52,
      h(0.58, v(0.58, leaf("recScript"), leaf("rec")), leaf("guide")),
      v(0.5, leaf("mic"), h(0.36, leaf("file"), leaf("wave"))),
    ),
  )
}

export function parseLayout(value: unknown): LayoutNode | null {
  const seen = new Set<string>()
  const node = readNode(value, seen)
  return node && seen.size > 0 ? node : null
}

export function leaf(id: string): LayoutNode {
  return { id }
}

export function contains(node: LayoutNode | null, id: string): boolean {
  if (!node)
    return false
  if ("id" in node)
    return node.id === id
  return contains(node.first, id) || contains(node.second, id)
}

export function showPane(root: LayoutNode | null, id: string, boxes: Record<string, PaneBox>): LayoutNode {
  if (!paneIds.includes(id))
    return root ?? leaf(id)
  if (root && contains(root, id))
    return root
  const added = leaf(id)
  if (!root)
    return added
  const target = largest(root, boxes)
  const box = boxes[target] ?? { width: 2, height: 1 }
  const split = box.width >= box.height ? h(0.5, leaf(target), added) : v(0.5, leaf(target), added)
  return replace(root, target, split)
}

export function hidePane(root: LayoutNode | null, id: string): LayoutNode | null {
  if (!root || !contains(root, id))
    return root
  return remove(root, id)
}

// 持ち上げで空いた領域は、接していた側だけが広げる。接していないパネルの幅と高さはそのままにする。
// 下限より狭いパネルだけは、空いた側から下限まで受け取ってよい。
export function liftPane(root: LayoutNode | null, id: string, span?: DockSpan): LayoutNode | null {
  if (!root || !contains(root, id))
    return root
  const width = span?.width ?? 0
  const height = span?.height ?? 0
  return detach(root, id, width, height)
}

export type DockEdge = "left" | "right" | "top" | "bottom"

export function movePane(
  root: LayoutNode | null,
  draggedId: string,
  targetIds: string[],
  edge: DockEdge,
  span?: DockSpan,
): LayoutNode | null {
  if (!root || !contains(root, draggedId))
    return root
  const stay = targetIds.filter((id) => id !== draggedId)
  if (stay.length === 0)
    return root
  const dragged = findPane(root, draggedId)
  if (!dragged)
    return root
  const without = liftPane(root, draggedId, span)
  if (!without)
    return dragged
  const resolved = smallestContaining(without, stay) ?? without
  const split = edge === "left"
    ? h(0.5, dragged, resolved)
    : edge === "right"
      ? h(0.5, resolved, dragged)
      : edge === "top"
        ? v(0.5, dragged, resolved)
        : v(0.5, resolved, dragged)
  const placed = replaceNode(without, resolved, split)
  if (!span || span.width < 1 || span.height < 1)
    return placed
  if (stay.length > 1)
    return fitGroupInsert(without, dragged, stay, edge, span, root)
  return preserveWidths(placed, root, draggedId, resolved, edge, span)
}

// 複数パネルを割るとき、入れたパネルは元の幅と高さより大きくせず、対象の半分も超えない。
// 割る範囲は、空白を埋めたあとの配置だけから決める。
function fitGroupInsert(
  lifted: LayoutNode,
  dragged: LayoutNode,
  stay: string[],
  edge: DockEdge,
  span: DockSpan,
  origin: LayoutNode,
): LayoutNode {
  if (!("id" in dragged))
    return lifted
  const host = smallestContaining(lifted, stay) ?? lifted
  const liftedRects = measureRects(lifted, span.width, span.height)
  const slice = edgeSubtree(host, edge)
  const sliceRect = unionRect(liftedRects, idsOf(slice))
  const drag = measureRects(origin, span.width, span.height).get(dragged.id)
  if (!sliceRect || !drag)
    return lifted
  const horizontal = edge === "left" || edge === "right"
  const hostMain = horizontal ? sliceRect.width : sliceRect.height
  const originalMain = horizontal ? drag.width : drag.height
  if (!(hostMain > 1))
    return lifted
  const floor = Math.min(paneLimit(hostMain), hostMain / 2)
  const size = Math.min(Math.max(Math.min(originalMain, hostMain / 2), floor), hostMain - floor)
  const share = size / hostMain
  const ratio = edge === "left" || edge === "top" ? share : 1 - share
  const split = edge === "left"
    ? h(ratio, dragged, slice)
    : edge === "right"
      ? h(ratio, slice, dragged)
      : edge === "top"
        ? v(ratio, dragged, slice)
        : v(ratio, slice, dragged)
  return replaceNode(lifted, slice, split)
}

// 挿入後も、入れたパネルと、その端に接するパネル以外は元の幅のままにする。
export function dropWidthFraction(
  root: LayoutNode | null,
  draggedId: string,
  targetIds: string[],
  edge: DockEdge,
  span?: DockSpan,
): number {
  if (!root || !span || edge === "top" || edge === "bottom")
    return 0.5
  const moved = movePane(root, draggedId, targetIds, edge, span)
  if (!moved)
    return 0.5
  const dragged = measureRects(moved, span.width, span.height).get(draggedId)
  const lifted = liftPane(root, draggedId, span)
  const stay = targetIds.filter((id) => id !== draggedId)
  if (!dragged || !lifted || stay.length === 0)
    return 0.5
  const host = smallestContaining(lifted, stay) ?? lifted
  const hostWidth = subtreeSize(lifted, host, span.width, span.height)?.width ?? 0
  if (!(hostWidth > 1))
    return 0.5
  return Math.min(0.9, Math.max(0.1, dragged.width / hostWidth))
}

export function commitMove(
  origin: LayoutNode | null,
  draggedId: string | null,
  targetIds: string[] | null,
  edge: DockEdge | null,
  span?: DockSpan,
): LayoutNode | null {
  if (!origin)
    return origin
  if (!draggedId || !targetIds || !edge)
    return origin
  const moved = movePane(origin, draggedId, targetIds, edge, span)
  if (moved && contains(moved, draggedId))
    return moved
  return origin
}

function preserveWidths(
  placed: LayoutNode,
  origin: LayoutNode,
  draggedId: string,
  target: LayoutNode,
  edge: DockEdge,
  span: DockSpan,
): LayoutNode {
  const lifted = liftPane(origin, draggedId, span)
  const filled = lifted ? measureRects(lifted, span.width, span.height) : new Map<string, PaneRect>()
  const previous = measureRects(origin, span.width, span.height)
  const desired = new Map<string, number>()
  for (const [id, rect] of filled)
    desired.set(id, rect.width)
  const dragged = previous.get(draggedId)
  if (dragged)
    desired.set(draggedId, dragged.width)
  const flex = new Set<string>()
  if (edge === "left" || edge === "right") {
    flex.add(draggedId)
    for (const id of edgeLeaves(target, edge))
      flex.add(id)
  }
  else if (fillsHeight(dragged, span.height)) {
    // 高さいっぱいの列を上か下に重ねるとき、空白を埋めたあとの列幅を使う。
    const stay = idsOf(target)
    const host = unionX(filled, stay)
    if (host)
      desired.set(draggedId, host.width)
    for (const [id] of filled)
      if (!stay.includes(id))
        flex.add(id)
    if (flex.size === 0)
      flex.add(draggedId)
  }
  else {
    flex.add(draggedId)
  }
  return applyWidths(placed, span.width, desired, flex, draggedId, span.width)
}

function fillsHeight(rect: PaneRect | undefined, height: number) {
  return rect != null && height > 1 && rect.height >= height - 1
}

function unionX(rects: Map<string, PaneRect>, ids: string[]) {
  let x0 = Number.POSITIVE_INFINITY
  let x1 = Number.NEGATIVE_INFINITY
  for (const id of ids) {
    const rect = rects.get(id)
    if (!rect)
      continue
    x0 = Math.min(x0, rect.x)
    x1 = Math.max(x1, rect.x + rect.width)
  }
  return x1 > x0 ? { x: x0, width: x1 - x0 } : null
}

function edgeLeaves(node: LayoutNode, edge: "left" | "right"): string[] {
  if ("id" in node)
    return [node.id]
  if (node.axis === "v")
    return [...edgeLeaves(node.first, edge), ...edgeLeaves(node.second, edge)]
  return edgeLeaves(edge === "left" ? node.first : node.second, edge)
}

function applyWidths(
  node: LayoutNode,
  available: number,
  desired: Map<string, number>,
  flex: Set<string>,
  draggedId: string,
  dock: number,
): LayoutNode {
  if ("id" in node || !(available > 0))
    return node
  if (node.axis === "v") {
    return {
      axis: "v",
      ratio: node.ratio,
      first: applyWidths(node.first, available, desired, flex, draggedId, dock),
      second: applyWidths(node.second, available, desired, flex, draggedId, dock),
    }
  }
  const pair = fitColumns(node.first, node.second, available, desired, flex, draggedId, dock)
  const ratio = Math.min(1 - 1e-4, Math.max(1e-4, pair.first / available))
  const firstWidth = ratio * available
  return {
    axis: "h",
    ratio,
    first: applyWidths(node.first, firstWidth, desired, flex, draggedId, dock),
    second: applyWidths(node.second, available - firstWidth, desired, flex, draggedId, dock),
  }
}

function fitColumns(
  first: LayoutNode,
  second: LayoutNode,
  available: number,
  desired: Map<string, number>,
  flex: Set<string>,
  draggedId: string,
  dock: number,
): { first: number; second: number } {
  const aLock = lockWidth(first, desired, flex)
  const bLock = lockWidth(second, desired, flex)
  if (aLock != null && bLock == null)
    return shareLocked(aLock, available, true)
  if (bLock != null && aLock == null)
    return shareLocked(bLock, available, false)
  const aWant = aLock ?? wantWidth(first, desired, flex)
  const bWant = bLock ?? wantWidth(second, desired, flex)
  const sum = aWant + bWant
  if (!(sum > 0))
    return { first: available / 2, second: available / 2 }
  if (sum <= available + 0.5) {
    const extra = Math.max(0, available - sum)
    const aFlex = hasFlex(first, flex)
    const bFlex = hasFlex(second, flex)
    if (bFlex && !aFlex)
      return { first: aWant, second: bWant + extra }
    if (aFlex && !bFlex)
      return { first: aWant + extra, second: bWant }
    const firstWidth = available * (aWant / sum)
    return { first: firstWidth, second: available - firstWidth }
  }
  let a = aWant
  let b = bWant
  let need = sum - available
  const shrinkEdgeFirst = contains(second, draggedId) && !contains(first, draggedId)
  const cut = (which: "a" | "b", min: number, flexSide: boolean) => {
    if (!flexSide || need <= 0)
      return
    const value = which === "a" ? a : b
    const spare = Math.max(0, value - min)
    const take = Math.min(spare, need)
    need -= take
    if (which === "a")
      a = value - take
    else
      b = value - take
  }
  const aMin = minWidth(first, desired, flex, dock)
  const bMin = minWidth(second, desired, flex, dock)
  if (shrinkEdgeFirst) {
    cut("a", aMin, hasFlex(first, flex))
    cut("b", bMin, hasFlex(second, flex))
  }
  else {
    cut("b", bMin, hasFlex(second, flex))
    cut("a", aMin, hasFlex(first, flex))
  }
  if (!(a > 0) || !(b > 0) || a + b <= 0)
    return { first: available / 2, second: available / 2 }
  if (Math.abs(a + b - available) > 0.5) {
    const firstWidth = available * (a / (a + b))
    return { first: firstWidth, second: available - firstWidth }
  }
  return { first: a, second: b }
}

function shareLocked(lock: number, available: number, lockIsFirst: boolean): { first: number; second: number } {
  const kept = Math.min(Math.max(1, lock), available - 1)
  return lockIsFirst
    ? { first: kept, second: available - kept }
    : { second: kept, first: available - kept }
}

function lockWidth(node: LayoutNode, desired: Map<string, number>, flex: Set<string>): number | null {
  if ("id" in node)
    return flex.has(node.id) ? null : desired.get(node.id) ?? null
  const first = lockWidth(node.first, desired, flex)
  const second = lockWidth(node.second, desired, flex)
  if (first == null || second == null)
    return null
  return node.axis === "v" ? Math.max(first, second) : first + second
}

function wantWidth(node: LayoutNode, desired: Map<string, number>, flex: Set<string>): number {
  if ("id" in node)
    return desired.get(node.id) ?? 0
  const first = wantWidth(node.first, desired, flex)
  const second = wantWidth(node.second, desired, flex)
  return node.axis === "v" ? Math.max(first, second) : first + second
}

function minWidth(node: LayoutNode, desired: Map<string, number>, flex: Set<string>, dock: number): number {
  if ("id" in node) {
    const width = desired.get(node.id) ?? 0
    return flex.has(node.id) ? Math.min(width, paneLimit(dock)) : width
  }
  const first = minWidth(node.first, desired, flex, dock)
  const second = minWidth(node.second, desired, flex, dock)
  return node.axis === "v" ? Math.max(first, second) : first + second
}

function hasFlex(node: LayoutNode, flex: Set<string>): boolean {
  if ("id" in node)
    return flex.has(node.id)
  return hasFlex(node.first, flex) || hasFlex(node.second, flex)
}

type PaneRect = { x: number; y: number; width: number; height: number }

function measureRects(node: LayoutNode, width: number, height: number): Map<string, PaneRect> {
  const found = new Map<string, PaneRect>()
  const walk = (current: LayoutNode, x: number, y: number, w: number, h: number) => {
    if ("id" in current) {
      found.set(current.id, { x, y, width: w, height: h })
      return
    }
    const span = current.axis === "h" ? w : h
    const size = span * current.ratio
    if (current.axis === "h") {
      walk(current.first, x, y, size, h)
      walk(current.second, x + size, y, Math.max(0, w - size), h)
      return
    }
    walk(current.first, x, y, w, size)
    walk(current.second, x, y + size, w, Math.max(0, h - size))
  }
  walk(node, 0, 0, width, height)
  return found
}

function unionRect(rects: Map<string, PaneRect>, ids: string[]): PaneRect | null {
  let x0 = Number.POSITIVE_INFINITY
  let y0 = Number.POSITIVE_INFINITY
  let x1 = Number.NEGATIVE_INFINITY
  let y1 = Number.NEGATIVE_INFINITY
  for (const id of ids) {
    const rect = rects.get(id)
    if (!rect)
      continue
    x0 = Math.min(x0, rect.x)
    y0 = Math.min(y0, rect.y)
    x1 = Math.max(x1, rect.x + rect.width)
    y1 = Math.max(y1, rect.y + rect.height)
  }
  return x1 > x0 && y1 > y0 ? { x: x0, y: y0, width: x1 - x0, height: y1 - y0 } : null
}

export function layoutHits(node: LayoutNode, span: DockSpan): { leaves: { id: string; rect: PaneRect }[]; regions: { ids: string[]; rect: PaneRect }[] } {
  const rects = measureRects(node, span.width, span.height)
  const leaves: { id: string; rect: PaneRect }[] = []
  const regions: { ids: string[]; rect: PaneRect }[] = []
  const walk = (current: LayoutNode) => {
    const ids = idsOf(current)
    const rect = unionRect(rects, ids)
    if (!rect)
      return
    regions.push({ ids, rect })
    if ("id" in current) {
      leaves.push({ id: current.id, rect })
      return
    }
    walk(current.first)
    walk(current.second)
  }
  walk(node)
  return { leaves, regions }
}

function edgeSubtree(node: LayoutNode, edge: DockEdge): LayoutNode {
  if ("id" in node)
    return node
  if (edge === "left" || edge === "right") {
    if (node.axis === "v")
      return node
    return edgeSubtree(edge === "left" ? node.first : node.second, edge)
  }
  if (node.axis === "h")
    return node
  return edgeSubtree(edge === "top" ? node.first : node.second, edge)
}

function subtreeSize(node: LayoutNode, find: LayoutNode, width: number, height: number): { width: number; height: number } | null {
  if (node === find)
    return { width, height }
  if ("id" in node)
    return null
  const span = node.axis === "h" ? width : height
  const size = span * node.ratio
  if (node.axis === "h") {
    return subtreeSize(node.first, find, size, height)
      ?? subtreeSize(node.second, find, Math.max(0, width - size), height)
  }
  return subtreeSize(node.first, find, width, size)
    ?? subtreeSize(node.second, find, width, Math.max(0, height - size))
}

function readNode(value: unknown, seen: Set<string>): LayoutNode | null {
  if (typeof value !== "object" || value === null)
    return null
  const record = value as Record<string, unknown>
  if (typeof record.id === "string") {
    if (!paneIds.includes(record.id) || seen.has(record.id))
      return null
    seen.add(record.id)
    return { id: record.id }
  }
  if (record.axis !== "h" && record.axis !== "v")
    return null
  if (typeof record.ratio !== "number" || !Number.isFinite(record.ratio) || record.ratio <= 0 || record.ratio >= 1)
    return null
  const first = readNode(record.first, seen)
  const second = readNode(record.second, seen)
  if (!first || !second)
    return null
  return { axis: record.axis, ratio: record.ratio, first, second }
}

function h(ratio: number, first: LayoutNode, second: LayoutNode): LayoutNode {
  return { axis: "h", ratio, first, second }
}

function v(ratio: number, first: LayoutNode, second: LayoutNode): LayoutNode {
  return { axis: "v", ratio, first, second }
}

function findPane(node: LayoutNode, id: string): LayoutNode | null {
  if ("id" in node)
    return node.id === id ? node : null
  return findPane(node.first, id) ?? findPane(node.second, id)
}

export function insertedRect(
  root: LayoutNode | null,
  draggedId: string,
  targetIds: string[],
  edge: DockEdge,
  span?: DockSpan,
): PaneRect | null {
  if (!root || !span)
    return null
  const moved = movePane(root, draggedId, targetIds, edge, span)
  if (!moved)
    return null
  return measureRects(moved, span.width, span.height).get(draggedId) ?? null
}

export function idsOf(node: LayoutNode): string[] {
  if ("id" in node)
    return [node.id]
  return [...idsOf(node.first), ...idsOf(node.second)]
}

function smallestContaining(node: LayoutNode, ids: string[]): LayoutNode | null {
  if (!ids.every((id) => contains(node, id)))
    return null
  if (!("id" in node)) {
    const inFirst = smallestContaining(node.first, ids)
    if (inFirst)
      return inFirst
    const inSecond = smallestContaining(node.second, ids)
    if (inSecond)
      return inSecond
  }
  return node
}

function replaceNode(node: LayoutNode, find: LayoutNode, replacement: LayoutNode): LayoutNode {
  if (node === find)
    return replacement
  if ("id" in node)
    return node
  return {
    ...node,
    first: replaceNode(node.first, find, replacement),
    second: replaceNode(node.second, find, replacement),
  }
}

function replace(node: LayoutNode, id: string, next: LayoutNode): LayoutNode {
  if ("id" in node)
    return node.id === id ? next : node
  return { ...node, first: replace(node.first, id, next), second: replace(node.second, id, next) }
}

function detach(node: LayoutNode, id: string, width: number, height: number): LayoutNode | null {
  if ("id" in node)
    return node.id === id ? null : node
  const horizontal = node.axis === "h"
  const span = horizontal ? width : height
  const firstSpan = span * node.ratio
  const secondSpan = span - firstSpan
  const first = detach(
    node.first,
    id,
    horizontal ? firstSpan : width,
    horizontal ? height : firstSpan,
  )
  const second = detach(
    node.second,
    id,
    horizontal ? secondSpan : width,
    horizontal ? height : secondSpan,
  )
  if (first === node.first && second === node.second)
    return node
  if (!first && !second)
    return null
  if (!first)
    return expandInto(second!, node.axis, 1 - node.ratio, true, span)
  if (!second)
    return expandInto(first, node.axis, node.ratio, false, span)
  return { axis: node.axis, ratio: node.ratio, first, second }
}

// oldShare は、広げる前のこの節が親の中で占めていた割合。newShare まで広げる。
// 増分は growAtStart のとき始端（左か上）、そうでないとき終端（右か下）に足す。
// parentPx はその親の実寸。0 のときは下限の受け渡しをしない。
function expandInto(
  node: LayoutNode,
  axis: "h" | "v",
  oldShare: number,
  growAtStart: boolean,
  parentPx: number,
): LayoutNode {
  return expandTo(node, axis, oldShare, 1, growAtStart, parentPx)
}

function expandTo(
  node: LayoutNode,
  axis: "h" | "v",
  oldShare: number,
  newShare: number,
  growAtStart: boolean,
  parentPx: number,
): LayoutNode {
  if (!(oldShare > 0) || !(newShare > 0) || Math.abs(newShare - oldShare) < 1e-9)
    return node
  if ("id" in node)
    return node
  if (node.axis !== axis) {
    const first = expandTo(node.first, axis, oldShare, newShare, growAtStart, parentPx)
    const second = expandTo(node.second, axis, oldShare, newShare, growAtStart, parentPx)
    if (first === node.first && second === node.second)
      return node
    return { axis: node.axis, ratio: node.ratio, first, second }
  }
  const firstOld = oldShare * node.ratio
  const secondOld = oldShare - firstOld
  const firstNew = growAtStart ? newShare - secondOld : firstOld
  const secondNew = newShare - firstNew
  const first = expandTo(node.first, axis, firstOld, firstNew, growAtStart, firstNew * parentPx)
  const second = expandTo(node.second, axis, secondOld, secondNew, growAtStart, secondNew * parentPx)
  if (!(parentPx > 1))
    return { axis, ratio: firstNew / newShare, first, second }
  const farIsSecond = growAtStart
  const farPx = (farIsSecond ? secondNew : firstNew) * parentPx
  const nearPx = parentPx - farPx
  const farNode = farIsSecond ? second : first
  const nearNode = farIsSecond ? first : second
  const deficit = shortfall(farNode, axis, farPx, paneLimit(parentPx))
  const shrunk = shrinkEdge(nearNode, axis, nearPx, !growAtStart, parentPx, deficit)
  if (shrunk.shrunk < 0.5)
    return { axis, ratio: firstNew / newShare, first, second }
  const grown = growShort(farNode, axis, farPx, farPx + shrunk.shrunk, paneLimit(parentPx))
  const nearPxNext = nearPx - shrunk.shrunk
  const farPxNext = farPx + shrunk.shrunk
  return {
    axis,
    ratio: (farIsSecond ? nearPxNext : farPxNext) / parentPx,
    first: farIsSecond ? shrunk.node : grown,
    second: farIsSecond ? grown : shrunk.node,
  }
}

function paneLimit(span: number) {
  return span <= minPane * 2 ? Math.max(1, span * 0.08) : minPane
}

function shortfall(node: LayoutNode, axis: "h" | "v", px: number, limit: number): number {
  if ("id" in node)
    return Math.max(0, limit - px)
  if (node.axis !== axis) {
    return Math.max(
      shortfall(node.first, axis, px, limit),
      shortfall(node.second, axis, px, limit),
    )
  }
  const firstPx = px * node.ratio
  return shortfall(node.first, axis, firstPx, limit) + shortfall(node.second, axis, px - firstPx, limit)
}

function growShort(node: LayoutNode, axis: "h" | "v", oldPx: number, newPx: number, limit: number): LayoutNode {
  const extra = newPx - oldPx
  if (!(extra > 0.5) || "id" in node)
    return node
  if (node.axis !== axis) {
    const first = growShort(node.first, axis, oldPx, newPx, limit)
    const second = growShort(node.second, axis, oldPx, newPx, limit)
    if (first === node.first && second === node.second)
      return node
    return { axis: node.axis, ratio: node.ratio, first, second }
  }
  const firstPx = oldPx * node.ratio
  const secondPx = oldPx - firstPx
  const firstShort = shortfall(node.first, axis, firstPx, limit)
  const secondShort = shortfall(node.second, axis, secondPx, limit)
  const total = firstShort + secondShort
  const firstGain = total > 0 ? extra * (firstShort / total) : 0
  const secondGain = extra - firstGain
  const firstNew = firstPx + firstGain
  const secondNew = secondPx + secondGain
  return {
    axis,
    ratio: firstNew / newPx,
    first: growShort(node.first, axis, firstPx, firstNew, limit),
    second: growShort(node.second, axis, secondPx, secondNew, limit),
  }
}

function shrinkEdge(
  node: LayoutNode,
  axis: "h" | "v",
  oldPx: number,
  fromEnd: boolean,
  span: number,
  want: number,
): { node: LayoutNode; shrunk: number } {
  if (!(want > 0.5))
    return { node, shrunk: 0 }
  const limit = paneLimit(span)
  if ("id" in node)
    return { node, shrunk: Math.min(want, Math.max(0, oldPx - limit)) }
  if (node.axis !== axis) {
    const spare = Math.min(
      edgeSpare(node.first, axis, oldPx, fromEnd, span),
      edgeSpare(node.second, axis, oldPx, fromEnd, span),
    )
    const shrunk = Math.min(want, spare)
    if (shrunk < 0.5)
      return { node, shrunk: 0 }
    return {
      node: {
        axis: node.axis,
        ratio: node.ratio,
        first: shrinkEdge(node.first, axis, oldPx, fromEnd, span, shrunk).node,
        second: shrinkEdge(node.second, axis, oldPx, fromEnd, span, shrunk).node,
      },
      shrunk,
    }
  }
  if (fromEnd) {
    const secondPx = oldPx * (1 - node.ratio)
    const firstPx = oldPx - secondPx
    const edge = shrinkEdge(node.second, axis, secondPx, true, span, want)
    if (edge.shrunk < 0.5)
      return { node, shrunk: 0 }
    return {
      node: { axis, ratio: firstPx / (oldPx - edge.shrunk), first: node.first, second: edge.node },
      shrunk: edge.shrunk,
    }
  }
  const firstPx = oldPx * node.ratio
  const edge = shrinkEdge(node.first, axis, firstPx, false, span, want)
  if (edge.shrunk < 0.5)
    return { node, shrunk: 0 }
  return {
    node: {
      axis,
      ratio: (firstPx - edge.shrunk) / (oldPx - edge.shrunk),
      first: edge.node,
      second: node.second,
    },
    shrunk: edge.shrunk,
  }
}

function edgeSpare(node: LayoutNode, axis: "h" | "v", oldPx: number, fromEnd: boolean, span: number): number {
  return shrinkEdge(node, axis, oldPx, fromEnd, span, Number.POSITIVE_INFINITY).shrunk
}

function remove(node: LayoutNode, id: string): LayoutNode | null {
  if ("id" in node)
    return node.id === id ? null : node
  const first = remove(node.first, id)
  const second = remove(node.second, id)
  if (!first)
    return second
  if (!second)
    return first
  if (first === node.first && second === node.second)
    return node
  return { ...node, first, second }
}

function largest(node: LayoutNode, boxes: Record<string, PaneBox>): string {
  let best = ""
  let area = -1
  const visit = (current: LayoutNode) => {
    if ("id" in current) {
      const box = boxes[current.id] ?? { width: 1, height: 1 }
      const next = box.width * box.height
      if (next > area) {
        area = next
        best = current.id
      }
      return
    }
    visit(current.first)
    visit(current.second)
  }
  visit(node)
  return best
}
