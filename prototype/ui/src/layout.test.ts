import { describe, expect, it } from "vitest"
import { commitMove, contains, createDefault, createRecording, hidePane, liftPane, movePane, paneTitles, parseLayout, showPane, viewFolders, type LayoutNode, type PaneBox } from "./layout"

describe("表示メニュー", () => {
  it("フォルダは既知のパネルだけで、どのパネルも一度は出る", () => {
    const ids = viewFolders.flatMap((folder) => folder.ids)
    for (const id of ids)
      expect(paneTitles[id], id).toBeTruthy()
    for (const id of Object.keys(paneTitles))
      expect(ids, id).toContain(id)
    for (const folder of viewFolders)
      expect(new Set(folder.ids).size, folder.title).toBe(folder.ids.length)
  })

  it("スペクトログラムは推定と原音設定にあり、id は一つ", () => {
    const titles = viewFolders.filter((folder) => folder.ids.includes("spec")).map((folder) => folder.title)
    expect(titles).toEqual(["原音設定の推定", "原音設定"])
    expect(paneTitles.spec).toBe("スペクトログラム")
  })
})

describe("初期配置", () => {
  const root = createDefault()

  it("製品の初期パネルだけを開く", () => {
    for (const id of ["file", "wave", "spec", "loud", "otoBand", "oto", "otoIni"])
      expect(contains(root, id)).toBe(true)
    expect(contains(root, "rec")).toBe(false)
    expect(contains(root, "mic")).toBe(false)
  })

  it("隠すと残り、足すと既存の葉を割る", () => {
    const hidden = hidePane(root, "otoIni")
    expect(contains(hidden, "otoIni")).toBe(false)
    expect(contains(hidden, "oto")).toBe(true)
    const shown = showPane(hidden, "mic", { oto: { width: 400, height: 200 } })
    expect(contains(shown, "mic")).toBe(true)
    expect(contains(shown, "file")).toBe(true)
  })
})

describe("録音の配置", () => {
  it("製品の録音テンプレートと同じパネルを開く", () => {
    const root = createRecording()
    for (const id of ["recList", "recScript", "rec", "guide", "mic", "file", "wave"])
      expect(contains(root, id)).toBe(true)
    expect(contains(root, "spec")).toBe(false)
  })
})

describe("パネルの移動", () => {
  const row: LayoutNode = {
    axis: "h",
    ratio: 0.4,
    first: { id: "file" },
    second: { id: "wave" },
  }

  it("左と右は横に半分で入れる", () => {
    expect(movePane(row, "wave", ["file"], "left")).toEqual({
      axis: "h",
      ratio: 0.5,
      first: { id: "wave" },
      second: { id: "file" },
    })
    expect(movePane(row, "file", ["wave"], "right")).toEqual({
      axis: "h",
      ratio: 0.5,
      first: { id: "wave" },
      second: { id: "file" },
    })
  })

  it("上と下は縦に半分で入れる", () => {
    expect(movePane(row, "wave", ["file"], "top")).toEqual({
      axis: "v",
      ratio: 0.5,
      first: { id: "wave" },
      second: { id: "file" },
    })
    expect(movePane(row, "wave", ["file"], "bottom")).toEqual({
      axis: "v",
      ratio: 0.5,
      first: { id: "file" },
      second: { id: "wave" },
    })
  })

  it("相手が自分だけのときと、離す場所が無いときは元の木のまま", () => {
    expect(movePane(row, "file", ["file"], "left")).toBe(row)
    expect(commitMove(row, "file", null, null)).toBe(row)
    expect(commitMove(row, "file", ["wave"], null)).toBe(row)
  })

  it("まとまりの端ではその枝を割る", () => {
    const tree: LayoutNode = {
      axis: "h",
      ratio: 0.3,
      first: { id: "file" },
      second: { axis: "v", ratio: 0.6, first: { id: "wave" }, second: { id: "spec" } },
    }
    expect(movePane(tree, "file", ["wave", "spec"], "right")).toEqual({
      axis: "h",
      ratio: 0.5,
      first: { axis: "v", ratio: 0.6, first: { id: "wave" }, second: { id: "spec" } },
      second: { id: "file" },
    })
  })

  it("接していないパネルの幅と高さは、持ち上げても残る", () => {
    const tree = v(0.2, leaf("wave"), v(0.4, leaf("spec"), v(0.5, leaf("loud"), leaf("otoBand"))))
    const before = boxes(tree, 800, 1000)
    const lifted = liftPane(tree, "wave", { width: 800, height: 1000 })
    const after = boxes(lifted!, 800, 1000)
    expect(after.loud.height).toBeCloseTo(before.loud.height, 4)
    expect(after.otoBand.height).toBeCloseTo(before.otoBand.height, 4)
    expect(after.spec.height).toBeCloseTo(before.spec.height + before.wave.height, 4)
    expect(after.loud.width).toBeCloseTo(before.loud.width, 4)
  })

  it("間にあるパネルの向こうは、持ち上げても幅を残す", () => {
    const tree = h(0.25, leaf("file"), v(0.5, h(0.5, leaf("wave"), leaf("spec")), leaf("loud")))
    const before = boxes(tree, 1000, 800)
    const lifted = liftPane(tree, "file", { width: 1000, height: 800 })
    const after = boxes(lifted!, 1000, 800)
    expect(after.spec.width).toBeCloseTo(before.spec.width, 4)
    expect(after.wave.width).toBeCloseTo(before.wave.width + before.file.width, 4)
    expect(after.loud.width).toBeCloseTo(before.loud.width + before.file.width, 4)
    expect(after.spec.height).toBeCloseTo(before.spec.height, 4)
    expect(after.loud.height).toBeCloseTo(before.loud.height, 4)
  })

  it("下限より狭いパネルだけ、空いた側から下限まで受け取る", () => {
    const tree = v(0.2, leaf("wave"), v(0.25, leaf("spec"), v(0.2, leaf("loud"), leaf("otoBand"))))
    const before = boxes(tree, 800, 1000)
    expect(before.loud.height).toBeCloseTo(120, 4)
    expect(before.otoBand.height).toBeCloseTo(480, 4)
    const lifted = liftPane(tree, "wave", { width: 800, height: 1000 })
    const after = boxes(lifted!, 800, 1000)
    expect(after.otoBand.height).toBeCloseTo(before.otoBand.height, 4)
    expect(after.loud.height).toBeCloseTo(160, 4)
    expect(after.spec.height).toBeCloseTo(before.spec.height + before.wave.height - 40, 4)
  })

  it("横に挿入しても、並んでいたパネルの幅は残る", () => {
    const tree = h(0.3, leaf("file"), v(0.6, leaf("wave"), leaf("spec")))
    const before = boxes(tree, 1000, 800)
    const moved = movePane(tree, "file", ["wave", "spec"], "right", { width: 1000, height: 800 })
    const after = boxes(moved!, 1000, 800)
    expect(after.file.width).toBeCloseTo(before.file.width, 3)
    expect(after.wave.width).toBeCloseTo(before.wave.width, 3)
    expect(after.spec.width).toBeCloseTo(before.spec.width, 3)
  })

  it("挿入した端の向こうのパネルは、幅を残す", () => {
    const tree = h(0.25, h(0.6, leaf("wave"), leaf("spec")), leaf("loud"))
    const before = boxes(tree, 1000, 800)
    const moved = movePane(tree, "loud", ["wave", "spec"], "left", { width: 1000, height: 800 })
    const after = boxes(moved!, 1000, 800)
    expect(after.wave.width).toBeCloseTo(before.wave.width, 3)
    expect(after.spec.width).toBeCloseTo(before.spec.width, 3)
    expect(after.loud.width).toBeCloseTo(before.loud.width, 3)
  })

  it("上に積んでも、隣の列の幅は残る", () => {
    const tree = h(0.3, leaf("file"), h(0.5, leaf("wave"), leaf("spec")))
    const before = boxes(tree, 1000, 800)
    const moved = movePane(tree, "file", ["wave"], "top", { width: 1000, height: 800 })
    const after = boxes(moved!, 1000, 800)
    expect(after.spec.width).toBeCloseTo(before.spec.width, 3)
  })

  it("離したあとも、接していないパネルの大きさを保つ", () => {
    const tree = h(0.2, leaf("file"), h(0.5, leaf("wave"), leaf("spec")))
    const before = boxes(tree, 1000, 600)
    const moved = movePane(tree, "file", ["wave"], "left", { width: 1000, height: 600 })
    const after = boxes(moved!, 1000, 600)
    expect(after.spec.width).toBeCloseTo(before.spec.width, 4)
  })

  it("知らない id や壊れた木は読み込まない", () => {
    expect(parseLayout(createDefault())).toEqual(createDefault())
    expect(parseLayout({ id: "missing" })).toBeNull()
    expect(parseLayout({ axis: "h", ratio: 0.5, first: { id: "file" }, second: { id: "file" } })).toBeNull()
  })
})

function boxes(node: LayoutNode, width: number, height: number): Record<string, PaneBox> {
  const found: Record<string, PaneBox> = {}
  const walk = (current: LayoutNode, w: number, h: number) => {
    if ("id" in current) {
      found[current.id] = { width: w, height: h }
      return
    }
    const span = current.axis === "h" ? w : h
    const size = span * current.ratio
    if (current.axis === "h") {
      walk(current.first, size, h)
      walk(current.second, w - size, h)
      return
    }
    walk(current.first, w, size)
    walk(current.second, w, h - size)
  }
  walk(node, width, height)
  return found
}

function leaf(id: string): LayoutNode {
  return { id }
}

function h(ratio: number, first: LayoutNode, second: LayoutNode): LayoutNode {
  return { axis: "h", ratio, first, second }
}

function v(ratio: number, first: LayoutNode, second: LayoutNode): LayoutNode {
  return { axis: "v", ratio, first, second }
}
