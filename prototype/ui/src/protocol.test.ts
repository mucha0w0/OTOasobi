import { describe, expect, it } from "vitest"
import { requestsPreview, shownInputRate, timeAt, xAt } from "./protocol"

describe("プレビューを頼む操作", () => {
  it("境界のドラッグとリサイズでは頼まない", () => {
    expect(requestsPreview("boundary")).toBe(false)
    expect(requestsPreview("resize")).toBe(false)
  })

  it("パンと拡大では頼む", () => {
    expect(requestsPreview("pan")).toBe(true)
    expect(requestsPreview("zoom")).toBe(true)
  })
})

describe("時刻と幅", () => {
  it("幅が変わっても同じ時刻は同じ位置の割合になる", () => {
    const time = 1.25
    const t0 = 0
    const t1 = 4
    expect(timeAt(xAt(time, 400, t0, t1), 400, t0, t1)).toBeCloseTo(time)
    expect(timeAt(xAt(time, 800, t0, t1), 800, t0, t1)).toBeCloseTo(time)
  })
})

describe("入力レート", () => {
  it("capture が伝えたレートだけを表示する", () => {
    expect(shownInputRate({ rate: 48000 })).toBe(48000)
    expect(shownInputRate(null)).toBeNull()
    expect(shownInputRate({ rate: 0 })).toBeNull()
  })
})
