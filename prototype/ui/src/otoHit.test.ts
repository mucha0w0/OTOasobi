import { describe, expect, it } from "vitest"
import { hitOtoPoint } from "./otoHit"
import type { OtoMark } from "./protocol"

const mark = (partial: Partial<OtoMark>): OtoMark => ({
  alias: "あ",
  offset: 0.2,
  overlap: 0.25,
  preutter: 0.3,
  consonant: 0.4,
  blank: 0.8,
  file: "あ.wav",
  index: 0,
  autoAlias: "あ",
  name: "",
  comment: "",
  tuned: [false, false, false, false, false],
  ...partial,
})

describe("oto の線", () => {
  it("近い線を掴む", () => {
    const hit = hitOtoPoint([mark({})], 100, 400, 0, 1)
    expect(hit).toEqual({ index: 0, point: 1 })
  })

  it("離れた位置は掴まれない", () => {
    expect(hitOtoPoint([mark({})], 10, 400, 0, 1)).toBeNull()
  })
})
