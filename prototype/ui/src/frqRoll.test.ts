import { describe, expect, it } from "vitest"
import { midiOf, noteName, pitchSpan } from "./frqRoll"

function hz(midi: number) {
  return 440 * 2 ** ((midi - 69) / 12)
}

describe("frq の表示音域", () => {
  it("有声が無いときは C4 の上下 2 オクターブ", () => {
    expect(pitchSpan([])).toEqual({ low: 36, high: 84, center: 60 })
    expect(noteName(36)).toBe("C2")
    expect(noteName(60)).toBe("C4")
    expect(noteName(84)).toBe("C6")
  })

  it("C4 だけのときは C-1 も C9 も入らない", () => {
    const span = pitchSpan([hz(60), 0, hz(60)])
    expect(span.low).toBe(36)
    expect(span.high).toBe(84)
    expect(span.low).toBeGreaterThan(0)
    expect(span.high).toBeLessThan(120)
  })

  it("中央から外れた点は、その高さまで広げる", () => {
    const span = pitchSpan([hz(60), hz(60), hz(100)])
    expect(span.center).toBeCloseTo(60, 5)
    expect(span.low).toBe(36)
    expect(span.high).toBe(100)
  })

  it("偶数個の中央は、真ん中 2 つの平均", () => {
    const span = pitchSpan([hz(48), hz(72)])
    expect(span.center).toBeCloseTo(60, 5)
    expect(midiOf(hz(60))).toBeCloseTo(60, 5)
  })
})
