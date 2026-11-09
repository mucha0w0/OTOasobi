import { describe, expect, it } from "vitest"
import { clampNoiseView, formatNoiseSeconds, samplePeaks, shadeRect, timeAtNoise, zoomNoiseView } from "./noiseWave"

describe("無音波形の表示範囲", () => {
  it("全体より長い表示はファイル全体になる", () => {
    expect(clampNoiseView(-1, 8, 4)).toEqual({ start: 0, length: 4 })
  })

  it("拡大はカーソル位置を基準にする", () => {
    const next = zoomNoiseView({ start: 0, length: 2 }, 1, 0.5, 4)
    expect(next.length).toBeCloseTo(1)
    expect(next.start).toBeCloseTo(0.5)
  })

  it("幅が変わっても同じ時刻は同じ位置の割合になる", () => {
    const view = { start: 0.5, length: 2 }
    const time = timeAtNoise(100, 400, view, 4)
    expect(timeAtNoise(200, 800, view, 4)).toBeCloseTo(time)
  })
})

describe("無音の選択", () => {
  it("表示範囲に重なるときだけ帯を出す", () => {
    const shade = shadeRect(0.8, 1.4, { start: 0.5, length: 2 }, 200, 88)
    expect(shade).toMatchObject({ y: 0, height: 88 })
    expect(shade?.x).toBeCloseTo(30)
    expect(shade?.width).toBeCloseTo(60)
    expect(shadeRect(3, 4, { start: 0, length: 1 }, 200, 88)).toBeNull()
  })

  it("秒数は小数2桁", () => {
    expect(formatNoiseSeconds(0.2, 0.7)).toBe("0.50秒")
  })

  it("列のピークは表示範囲の最小と最大", () => {
    const lo = [0, -0.4, -0.1, 0]
    const hi = [0.1, 0.2, 0.8, 0.1]
    expect(samplePeaks(lo, hi, 4, 1, 3)).toEqual({ min: -0.4, max: 0.8 })
  })
})
