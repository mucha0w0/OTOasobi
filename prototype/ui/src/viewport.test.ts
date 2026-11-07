import { describe, expect, it } from "vitest"
import { imagePlacement, panRange, zoomRange } from "./viewport"

describe("表示範囲", () => {
  it("拡大してもポインタの時刻は同じ割合に残る", () => {
    const anchor = 1
    const next = zoomRange({ t0: 0, t1: 4 }, anchor, 0.8, 10)
    expect(next.t1 - next.t0).toBeCloseTo(3.2)
    expect((anchor - next.t0) / (next.t1 - next.t0)).toBeCloseTo(0.25)
  })

  it("端を超えてパンしない", () => {
    expect(panRange({ t0: 0, t1: 2 }, -0.5, 4)).toEqual({ t0: 0, t1: 2 })
    expect(panRange({ t0: 2, t1: 4 }, 0.5, 4).t1).toBeCloseTo(4)
  })

  it("今の画像を新しい範囲へ置く", () => {
    const placed = imagePlacement({ t0: 0, t1: 4 }, { t0: 1, t1: 3 }, 200)
    expect(placed.x).toBeCloseTo(-100)
    expect(placed.width).toBeCloseTo(400)
  })
})
