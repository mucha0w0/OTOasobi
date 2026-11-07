import { describe, expect, it } from "vitest"
import { shouldReplaceBitmap } from "./bitmap"

describe("キャンバスのバッファ", () => {
  it("ドラッグでサイズが変わっても作り直さない", () => {
    expect(shouldReplaceBitmap(true, 800, 400, 900, 400)).toBe(false)
  })

  it("離したあとにサイズが違えば作り直す", () => {
    expect(shouldReplaceBitmap(false, 800, 400, 900, 400)).toBe(true)
    expect(shouldReplaceBitmap(false, 800, 400, 800, 400)).toBe(false)
  })
})
