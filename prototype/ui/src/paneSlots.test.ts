import { describe, expect, it } from "vitest"
import { assignHost } from "./paneSlots"

describe("パネルの置き場", () => {
  it("同じ要素なら置き場を変えない", () => {
    const element = {}
    const current = { wave: element }
    expect(assignHost(current, "wave", element)).toBe(current)
  })

  it("外した id は消し、残った面は残す", () => {
    const wave = {}
    const spec = {}
    expect(assignHost({ wave, spec }, "wave", null)).toEqual({ spec })
  })

  it("無い id を外しても、元の対応を返す", () => {
    const current = { spec: {} }
    expect(assignHost(current, "wave", null)).toBe(current)
  })
})
