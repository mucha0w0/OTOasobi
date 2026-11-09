import { describe, expect, it } from "vitest"
import { halfBox, hitDrop, previewBox, type HitLeaf, type HitRegion } from "./dockHit"

const leaf: HitLeaf = { id: "wave", rect: { x: 0, y: 0, width: 400, height: 300 } }
const outer: HitRegion = { ids: ["wave", "spec"], rect: { x: 0, y: 0, width: 400, height: 300 } }
const inner: HitRegion = { ids: ["wave"], rect: { x: 0, y: 0, width: 400, height: 180 } }

describe("ドロップ先", () => {
  it("中央の印は、その1枚だけを割る", () => {
    const aim = hitDrop([leaf], [outer, inner], "file", 200 - 6 - 14, 150)
    expect(aim?.edge).toBe("left")
    expect(aim?.targetIds).toEqual(["wave"])
    expect(aim?.hot).toBe(true)
  })

  it("端の帯は、同じ距離なら大きいまとまりを割る", () => {
    const aim = hitDrop([leaf], [inner, outer], "file", 10, 150)
    expect(aim?.edge).toBe("left")
    expect(aim?.targetIds).toEqual(["wave", "spec"])
    expect(aim?.hot).toBe(false)
  })

  it("右へ入れる予告は、対象の右端から幅だけ戻る", () => {
    const half = halfBox({ x: 100, y: 40, width: 400, height: 200 }, "right")
    expect(previewBox(half, "right", 0.5)).toEqual({ x: 300, y: 40, width: 200, height: 200 })
    expect(previewBox(half, "right", 0.25)).toEqual({ x: 400, y: 40, width: 100, height: 200 })
  })

  it("左へ入れる予告は、対象の左端から始まる", () => {
    const half = halfBox({ x: 100, y: 40, width: 400, height: 200 }, "left")
    expect(previewBox(half, "left", 0.25)).toEqual({ x: 100, y: 40, width: 100, height: 200 })
  })

  it("印でも端でもない中央では入れない", () => {
    const aim = hitDrop([leaf], [outer], "file", 200, 150)
    expect(aim?.edge).toBeNull()
    expect(aim?.arms).toEqual(leaf.rect)
  })
})
