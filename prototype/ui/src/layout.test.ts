import { describe, expect, it } from "vitest"
import { commitMove, contains, createDefault, hidePane, movePane, parseLayout, showPane, type LayoutNode } from "./layout"

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
