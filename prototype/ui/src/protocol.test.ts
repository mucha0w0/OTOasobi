import { describe, expect, it } from "vitest"
import { parseHostEvent, requestsPreview, requestsPreviewAfter, shownInputRate, timeAt, xAt } from "./protocol"

describe("プレビューを頼む操作", () => {
  it("境界のドラッグとリサイズの途中では頼まない", () => {
    expect(requestsPreview("boundary")).toBe(false)
    expect(requestsPreview("resize")).toBe(false)
    expect(requestsPreviewAfter("boundary")).toBe(false)
  })

  it("パンは離したときに頼む", () => {
    expect(requestsPreview("pan")).toBe(true)
    expect(requestsPreview("zoom")).toBe(false)
  })

  it("拡大縮小は止めたあと、リサイズは離したあとに頼む", () => {
    expect(requestsPreviewAfter("resize")).toBe(true)
    expect(requestsPreviewAfter("pan")).toBe(true)
    expect(requestsPreviewAfter("zoom")).toBe(true)
  })
})

describe("時刻と幅", () => {
  it("幅が変わっても同じ時刻は同じ位置の割合になる", () => {
    const time = 1.25
    const t0 = 0
    const t1 = 4
    expect(timeAt(xAt(time, 400, t0, t1), 400, t0, t1)).toBeCloseTo(time)
    expect(timeAt(xAt(time, 800, t0, t1), 800, t0, t1)).toBeCloseTo(time)
  })
})

describe("入力レート", () => {
  it("capture が伝えたレートだけを表示する", () => {
    expect(shownInputRate({ rate: 48000 })).toBe(48000)
    expect(shownInputRate(null)).toBeNull()
    expect(shownInputRate({ rate: 0 })).toBeNull()
  })
})

describe("ホストのメッセージ", () => {
  it("ファイル一覧は名前だけを足す", () => {
    expect(parseHostEvent({ type: "files", folder: "C:/voice", names: ["あ.wav", 1, "い.wav"] })).toEqual({
      type: "files",
      folder: "C:/voice",
      names: ["あ.wav", "い.wav"],
    })
  })

  it("preview と saved の形は変えない", () => {
    const preview = parseHostEvent({
      type: "preview",
      duration: 2,
      lo: [0],
      hi: [1],
      png: "abc",
      specT0: 0,
      specT1: 2,
      waveT0: 0,
      waveT1: 2,
      sampleRate: 44100,
    })
    expect(preview).toMatchObject({ type: "preview", png: "abc", sampleRate: 44100, loudness: [] })
    expect(preview && "names" in preview).toBe(false)

    expect(parseHostEvent({
      type: "saved",
      path: "take.wav",
      inputRate: 48000,
      headerRate: 44100,
      bits: 16,
      channels: 1,
      seconds: 1,
      elapsed: 1.2,
    })).toEqual({
      type: "saved",
      path: "take.wav",
      inputRate: 48000,
      headerRate: 44100,
      bits: 16,
      channels: 1,
      seconds: 1,
      elapsed: 1.2,
    })
  })

  it("oto の区間を読み、壊れた計算方法は捨てる", () => {
    expect(parseHostEvent({ type: "oto", mode: "other", scope: "all" })).toBeNull()
    expect(parseHostEvent({
      type: "oto",
      mode: "mono",
      scope: "all",
      marks: [{ alias: "あ", offset: 0.1, overlap: 0.2, preutter: 0.3, consonant: 0.4, blank: 0.5 }],
    })).toMatchObject({
      type: "oto",
      marks: [{ alias: "あ", offset: 0.1, blank: 0.5 }],
    })
  })
})
