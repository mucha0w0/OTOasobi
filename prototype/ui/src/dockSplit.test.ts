import { describe, expect, it } from "vitest"
import { pickTitleResize } from "./dockSplit"

const open = { left: true, right: true, top: true }

describe("見出しの端", () => {
  it("上の分割に近い側はリサイズ", () => {
    expect(pickTitleResize(40, 4, 200, open)).toBe("top")
  })

  it("見出しの下半分は移動のまま", () => {
    expect(pickTitleResize(40, 24, 200, open)).toBeNull()
  })

  it("左右の分割に近い側はリサイズ", () => {
    expect(pickTitleResize(4, 24, 200, open)).toBe("left")
    expect(pickTitleResize(190, 24, 200, open)).toBe("right")
  })

  it("その向きに分割が無ければ移動", () => {
    expect(pickTitleResize(4, 4, 200, { left: false, right: false, top: false })).toBeNull()
    expect(pickTitleResize(4, 20, 200, { left: false, right: true, top: true })).toBeNull()
  })

  it("角ではより近い辺を取る", () => {
    expect(pickTitleResize(2, 14, 200, open)).toBe("left")
    expect(pickTitleResize(11, 2, 200, open)).toBe("top")
  })
})
