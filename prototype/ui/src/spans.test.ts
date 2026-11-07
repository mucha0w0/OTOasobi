import { describe, expect, it } from "vitest"
import { hitBoundary, isQuietLabel, isVowelLabel, moveBoundary, spanBoundaries, spanCaption, spanFill, type PhoneSpan } from "./spans"

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

  it("境界をドラッグすると隣の区間と時刻を分け、5ms より狭くしない", () => {
    const spans: PhoneSpan[] = [
      { start: 0, end: 0.2, label: "無音", token: "" },
      { start: 0.2, end: 0.6, label: "k", token: "か" },
      { start: 0.6, end: 1, label: "a", token: "か" },
    ]
    expect(hitBoundary(spans, 0, 100, 0, 1)).toBe(-1)
    const group = hitBoundary(spans, 60, 100, 0, 1)
    expect(group).toBeGreaterThan(0)
    const moved = moveBoundary(spans, group, 0.7, 1)
    expect(moved[1]?.end).toBeCloseTo(0.7)
    expect(moved[2]?.start).toBeCloseTo(0.7)
    expect(moved[0]).toEqual(spans[0])
    const clamped = moveBoundary(spans, group, 0.999, 1)
    expect(clamped[1]?.end).toBeCloseTo(0.995)
    expect(clamped[2]?.start).toBeCloseTo(0.995)
  })
})