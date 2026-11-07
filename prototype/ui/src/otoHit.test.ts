import { describe, expect, it } from "vitest"
import { cycleOtoFocus, focusedOwnsTime, hitOtoPoint } from "./otoHit"
import type { OtoMark } from "./protocol"
import type { PhoneSpan } from "./spans"

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

  it("重なった音素は狭いほうから始まり、クリックのたびに次へ回る", () => {
    const wide = mark({ index: 0, offset: 0.1, overlap: 0.12, preutter: 0.15, consonant: 0.22, blank: 0.9 })
    const narrow = mark({ index: 1, alias: "い", offset: 0.3, overlap: 0.32, preutter: 0.4, consonant: 0.5, blank: 0.7 })
    const marks = [wide, narrow]
    expect(cycleOtoFocus(marks, 240, 400, 0, 1, -1)).toBe(1)
    expect(cycleOtoFocus(marks, 240, 400, 0, 1, 1)).toBe(0)
    expect(cycleOtoFocus(marks, 240, 400, 0, 1, 0)).toBe(1)
    expect(cycleOtoFocus(marks, 40, 400, 0, 1, -1)).toBe(0)
    expect(cycleOtoFocus(marks, 10, 400, 0, 1, 0)).toBe(-1)
  })

  it("強調中の母音は直前の子音も自分の区間にする", () => {
    const spans: PhoneSpan[] = [
      { start: 0, end: 0.1, label: "無音", token: "" },
      { start: 0.1, end: 0.2, label: "k", token: "か" },
      { start: 0.2, end: 0.5, label: "a", token: "か" },
    ]
    const marks = [mark({ index: 0, offset: 0.05, preutter: 0.2, blank: 0.5 })]
    expect(focusedOwnsTime(marks, spans, 0, 0.15)).toBe(true)
    expect(focusedOwnsTime(marks, spans, 0, 0.3)).toBe(true)
    expect(focusedOwnsTime(marks, spans, 0, 0.05)).toBe(false)
  })
})
