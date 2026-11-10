import { describe, expect, it } from "vitest"
import { nextFileIndex, takeWheelSteps } from "./fileStep"

describe("ホイールでのファイル移動", () => {
  it("ノッチの数だけ進み、端で止まる", () => {
    expect(nextFileIndex(2, 10, 1)).toBe(3)
    expect(nextFileIndex(2, 10, 5)).toBe(7)
    expect(nextFileIndex(2, 10, -4)).toBe(0)
    expect(nextFileIndex(8, 10, 5)).toBe(9)
  })

  it("選択が無いときは先頭から数える", () => {
    expect(nextFileIndex(-1, 4, 2)).toBe(2)
    expect(nextFileIndex(0, 0, 3)).toBe(-1)
  })

  it("画素ホイールの1ノッチは1段で、端数は次のノッチに混ぜない", () => {
    expect(takeWheelSteps(0, 100, 0)).toEqual({ steps: 1, carry: 0 })
    expect(takeWheelSteps(0, -120, 0)).toEqual({ steps: -1, carry: 0 })
    expect(takeWheelSteps(80, 100, 0)).toEqual({ steps: 1, carry: 0 })
    expect(takeWheelSteps(0, 16, 0)).toEqual({ steps: 0, carry: 16 })
    expect(takeWheelSteps(90, 16, 0)).toEqual({ steps: 1, carry: 0 })
  })

  it("行モードは行数だけ進む", () => {
    expect(takeWheelSteps(0, 1, 1)).toEqual({ steps: 1, carry: 0 })
    expect(takeWheelSteps(0, 3, 1)).toEqual({ steps: 3, carry: 0 })
  })
})
