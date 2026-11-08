import { describe, expect, it } from "vitest"
import { formatTick, niceStep, rulerTicks } from "./ruler"

describe("時間ルーラー", () => {
  it("目盛りは 1、2、5 の倍数", () => {
    expect(niceStep(1, 800)).toBeCloseTo(0.1)
    expect(niceStep(10, 800)).toBeCloseTo(1)
    expect(niceStep(30, 800)).toBeCloseTo(5)
    expect(niceStep(90, 800)).toBeCloseTo(10)
  })

  it("ラベルは秒で、端では切る", () => {
    expect(formatTick(0.25, 0.05)).toBe("0.250 s")
    expect(formatTick(2, 1)).toBe("2 s")
    const ticks = rulerTicks(0, 1, 800)
    expect(ticks.length).toBeGreaterThan(2)
    expect(ticks.every((tick) => tick.label.endsWith(" s"))).toBe(true)
    expect(ticks[0]!.time).toBeGreaterThan(0)
    expect(ticks.at(-1)!.time).toBeLessThan(1)
  })
})
