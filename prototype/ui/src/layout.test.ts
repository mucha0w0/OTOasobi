import { describe, expect, it } from "vitest"
import { commitMove, contains, createDefault, createRecording, hidePane, leaf, movePane, paneTitles, parseLayout, rulerPaneIds, showPane, viewFolders, type LayoutNode } from "./layout"

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

describe("時間ルーラーの位置", () => {
  function h(ratio: number, first: LayoutNode, second: LayoutNode): LayoutNode {
    return { axis: "h", ratio, first, second }
  }
  function v(ratio: number, first: LayoutNode, second: LayoutNode): LayoutNode {
    return { axis: "v", ratio, first, second }
  }

  it("縦に積んだグラフは上の 1 枚だけ", () => {
    expect(rulerPaneIds(createDefault())).toEqual(["wave"])
  })

  it("左右に分かれたグラフは列ごとに出す", () => {
    expect(rulerPaneIds(h(0.5, leaf("wave"), leaf("spec")))).toEqual(["wave", "spec"])
    expect(rulerPaneIds(h(0.5, v(0.5, leaf("wave"), leaf("spec")), leaf("loud")))).toEqual(["wave", "loud"])
  })

  it("上に別のパネルがあっても、その列のグラフの先頭に出す", () => {
    expect(rulerPaneIds(v(0.4, leaf("file"), leaf("otoBand")))).toEqual(["otoBand"])
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

  it("知らない id や壊れた木は読み込まない", () => {
    expect(parseLayout(createDefault())).toEqual(createDefault())
    expect(parseLayout({ id: "missing" })).toBeNull()
    expect(parseLayout({ axis: "h", ratio: 0.5, first: { id: "file" }, second: { id: "file" } })).toBeNull()
  })
})
