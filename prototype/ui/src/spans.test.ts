import { describe, expect, it } from "vitest"
import { isQuietLabel, isVowelLabel, spanBoundaries, spanCaption, spanFill } from "./spans"

describe("音素のラベル", () => {
  it("トークンがあるときは音素名と並べる", () => {
    expect(spanCaption({ label: "a", token: "あ" })).toBe("あ  a")
    expect(spanCaption({ label: "a", token: "_" })).toBe("a")
    expect(spanCaption({ label: "a", token: "a" })).toBe("a")
    expect(spanCaption({ label: "無音", token: "" })).toBe("無音")
  })

  it("無音と息は塗らない", () => {
    expect(isQuietLabel("無音")).toBe(true)
    expect(isQuietLabel("息")).toBe(true)
    expect(spanFill("無音", true)).toBeNull()
    expect(isVowelLabel("a")).toBe(true)
    expect(isVowelLabel("k")).toBe(false)
    expect(spanFill("a", true)).toContain("15, 157, 142")
    expect(spanFill("k", false)).toContain("217, 119, 6")
  })

  it("無音だけの境界は消し、母音の端は残す", () => {
    const times = spanBoundaries([
      { start: 0, end: 0.1, label: "無音", token: "" },
      { start: 0.1, end: 0.4, label: "a", token: "あ" },
    ])
    expect(times.some((time) => time < 0.05)).toBe(false)
    expect(times).toContain(0.1)
    expect(times).toContain(0.4)
  })
})