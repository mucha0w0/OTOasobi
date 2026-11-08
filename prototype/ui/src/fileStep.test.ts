import { describe, expect, it } from "vitest"
import { nextFileIndex } from "./fileStep"

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
})
