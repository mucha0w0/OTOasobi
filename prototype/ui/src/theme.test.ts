import { describe, expect, it } from "vitest"
import { buttonColors, contrastRatio, parseHex } from "./theme"

describe("アクセントの文字色", () => {
  it("白、黒、明るい黄でも塗りと文字のコントラストが 4.5 以上になる", () => {
    for (const hex of ["#ffffff", "#000000", "#ffe600"]) {
      const colors = buttonColors(hex)
      const fill = parseHex(colors.fill)
      const ink = parseHex(colors.ink)
      expect(fill).not.toBeNull()
      expect(ink).not.toBeNull()
      expect(contrastRatio(fill!, ink!)).toBeGreaterThanOrEqual(4.5)
    }
  })
})
