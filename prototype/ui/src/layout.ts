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

export function setRatio(node: LayoutNode, path: number[], ratio: number): LayoutNode {
  if ("id" in node)
    return node
  if (path.length === 0)
    return { ...node, ratio }
  const [head, ...rest] = path
  return head === 0
    ? { ...node, first: setRatio(node.first, rest, ratio) }
    : { ...node, second: setRatio(node.second, rest, ratio) }
}

function h(ratio: number, first: LayoutNode, second: LayoutNode): LayoutNode {
  return { axis: "h", ratio, first, second }
}

function v(ratio: number, first: LayoutNode, second: LayoutNode): LayoutNode {
  return { axis: "v", ratio, first, second }
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
