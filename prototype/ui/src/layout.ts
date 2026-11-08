export type LayoutNode =
  | { id: string }
  | { axis: "h" | "v"; ratio: number; first: LayoutNode; second: LayoutNode }

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
  recList: "録音リスト",
  recScript: "録音スクリプト",
  rec: "録音コントローラ",
  guide: "ガイドBGM設定",
  mic: "マイクモニター",
}

// 表示メニューのフォルダ。同じ id は複数に置いてよく、開閉は id で揃う。
export const viewFolders: { title: string; ids: string[] }[] = [
  { title: "録音", ids: ["file", "recList", "recScript", "rec", "guide", "mic"] },
  { title: "原音設定の推定", ids: ["file", "source", "wave", "spec", "loud", "oto"] },
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

const graphPaneIds = new Set(["wave", "spec", "loud", "otoBand"])

type PlacedPane = { id: string; x: number; y: number; w: number; h: number }

/** 各列でいちばん上にあるグラフ面。縦積みは 1 本、左右に分かれた列は列ごと。 */
export function rulerPaneIds(root: LayoutNode | null): string[] {
  if (!root)
    return []
  const placed: PlacedPane[] = []
  placeLayout(root, 0, 0, 1, 1, placed)
  const graphs = placed.filter((pane) => graphPaneIds.has(pane.id))
  return graphs
    .filter((pane) => !graphs.some((other) => other.id !== pane.id && sharesColumn(pane, other) && other.y + other.h <= pane.y + 1e-6))
    .map((pane) => pane.id)
}

function sharesColumn(pane: PlacedPane, other: PlacedPane) {
  const left = Math.max(pane.x, other.x)
  const right = Math.min(pane.x + pane.w, other.x + other.w)
  return right - left > Math.min(pane.w, other.w) * 0.5
}

function placeLayout(node: LayoutNode, x: number, y: number, w: number, h: number, out: PlacedPane[]) {
  if ("id" in node) {
    out.push({ id: node.id, x, y, w, h })
    return
  }
  if (node.axis === "h") {
    placeLayout(node.first, x, y, w * node.ratio, h, out)
    placeLayout(node.second, x + w * node.ratio, y, w * (1 - node.ratio), h, out)
    return
  }
  placeLayout(node.first, x, y, w, h * node.ratio, out)
  placeLayout(node.second, x, y + h * node.ratio, w, h * (1 - node.ratio), out)
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

export type DockEdge = "left" | "right" | "top" | "bottom"

export function movePane(root: LayoutNode | null, draggedId: string, targetIds: string[], edge: DockEdge): LayoutNode | null {
  if (!root || !contains(root, draggedId))
    return root
  const stay = targetIds.filter((id) => id !== draggedId)
  if (stay.length === 0)
    return root
  const dragged = findPane(root, draggedId)
  if (!dragged)
    return root
  const without = remove(root, draggedId)
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
  return replaceNode(without, resolved, split)
}

export function commitMove(
  origin: LayoutNode | null,
  draggedId: string | null,
  targetIds: string[] | null,
  edge: DockEdge | null,
): LayoutNode | null {
  if (!origin)
    return origin
  if (!draggedId || !targetIds || !edge)
    return origin
  const moved = movePane(origin, draggedId, targetIds, edge)
  if (moved && contains(moved, draggedId))
    return moved
  return origin
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
