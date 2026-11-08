import { describe, expect, it } from "vitest"
import { decodeBase64, toneLut } from "./specTone"

describe("スペクトログラムの色", () => {
  it("端の色はグラデーションの端と同じ", () => {
    const lut = toneLut()
    expect([...lut.slice(0, 3)]).toEqual([0, 0, 4])
    expect([...lut.slice(255 * 3, 256 * 3)]).toEqual([252, 253, 191])
  })

  it("色番号の列をバイトのまま戻す", () => {
    expect([...decodeBase64(btoa("\x00\xff"))]).toEqual([0, 255])
  })
})
