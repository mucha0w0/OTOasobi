import { describe, expect, it } from "vitest"
import { buttonColors, contrastRatio, correctAccent, defaultAccent, oklchOf, parseHex } from "./theme"

describe("アクセントの文字色", () => {
  it("ライトでは文字を白にし、白とのコントラストを 4.5 以上にする", () => {
    for (const hex of ["#ffffff", "#000000", "#ffe600", "#0f766e", "#ff66cc"]) {
      const colors = buttonColors(correctAccent(hex, "light"), "light")
      expect(colors.ink).toBe("#ffffff")
      const fill = parseHex(colors.fill)
      const ink = parseHex(colors.ink)
      expect(fill).not.toBeNull()
      expect(ink).not.toBeNull()
      expect(contrastRatio(fill!, ink!)).toBeGreaterThanOrEqual(4.5)
    }
  })

  it("ダークでは白と黒のうちコントラストが高い方を選ぶ", () => {
    for (const hex of ["#ffffff", "#000000", "#ffe600"]) {
      const colors = buttonColors(correctAccent(hex, "dark"), "dark")
      const fill = parseHex(colors.fill)
      const ink = parseHex(colors.ink)
      expect(fill).not.toBeNull()
      expect(ink).not.toBeNull()
      expect(contrastRatio(fill!, ink!)).toBeGreaterThanOrEqual(4.5)
    }
  })
})

describe("アクセントの明度", () => {
  it("範囲の中にある色は、その色のまま", () => {
    for (const [hex, theme] of [["#0f766e", "light"], ["#2bb8a0", "dark"]] as const) {
      const oklch = oklchOf(hex)
      expect(oklch).not.toBeNull()
      const [min, max] = theme === "dark" ? [0.65, 0.85] : [0.38, 0.58]
      expect(oklch!.L).toBeGreaterThanOrEqual(min)
      expect(oklch!.L).toBeLessThanOrEqual(max)
      expect(correctAccent(hex, theme)).toBe(hex)
      expect(defaultAccent(theme)).toBe(hex)
    }
  })

  it("範囲の外では明度だけを収め、色相は残す", () => {
    const source = "#7f1d1d"
    const before = oklchOf(source)
    const corrected = correctAccent(source, "dark")
    const after = oklchOf(corrected)
    expect(before).not.toBeNull()
    expect(after).not.toBeNull()
    expect(before!.L).toBeLessThan(0.65)
    expect(after!.L).toBeGreaterThanOrEqual(0.65 - 0.02)
    expect(after!.L).toBeLessThanOrEqual(0.85 + 0.02)
    expect(hueGap(before!.H, after!.H)).toBeLessThan(2)
    const light = oklchOf(correctAccent("#ffe600", "light"))
    expect(light!.L).toBeLessThanOrEqual(0.58 + 0.02)
    expect(light!.L).toBeGreaterThanOrEqual(0.38 - 0.02)
  })
})

function hueGap(left: number, right: number) {
  const gap = Math.abs(left - right) % 360
  return Math.min(gap, 360 - gap)
}
