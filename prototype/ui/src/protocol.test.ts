import { describe, expect, it } from "vitest"
import { parseHostEvent, previewStillCovers, requestsPreview, requestsPreviewAfter, shownInputRate, timeAt, uiScale, xAt } from "./protocol"
import { decodeBase64 } from "./specTone"

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
      file: 0,
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
    expect(preview && preview.type === "preview" && [...preview.indices]).toEqual([])
    expect(preview).toMatchObject({ type: "preview", name: "", png: "abc", sampleRate: 44100, loudness: [], file: 0 })

    const indexed = parseHostEvent({
      type: "preview",
      duration: 2,
      lo: [0],
      hi: [1],
      spec: "qqo=",
      specColumns: 2,
      specRows: 1,
      waveT0: 0,
      waveT1: 2,
    })
    expect(indexed && indexed.type === "preview" && [...indexed.indices]).toEqual([...decodeBase64("qqo=")])
    expect(indexed).toMatchObject({ type: "preview", name: "", png: "", specColumns: 2, specRows: 1 })
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
      spans: [{ label: "a", token: "あ", start: 0.2, end: 0.4 }],
    })).toMatchObject({
      type: "oto",
      marks: [{ alias: "あ", offset: 0.1, blank: 0.5 }],
      spans: [{ label: "a", token: "あ", start: 0.2, end: 0.4 }],
    })
    expect(parseHostEvent({ type: "oto", mode: "mono", scope: "all", generation: 4 })).toMatchObject({
      generation: 4,
      rows: null,
    })
    const named = parseHostEvent({
      type: "preview",
      name: "あ.wav",
      duration: 1,
      lo: [0],
      hi: [1],
      spec: "qqo=",
      specColumns: 2,
      specRows: 1,
      specT0: 0,
      specT1: 1,
      waveT0: 0,
      waveT1: 1,
    })
    expect(named && named.type === "preview" && [...named.indices]).toEqual([...decodeBase64("qqo=")])
  })

  it("同じ範囲と列数の絵は取り直さない", () => {
    const picture = { indices: decodeBase64("qqo="), specColumns: 2, specRows: 1, specT0: 0, specT1: 1, waveT0: 0, waveT1: 1 }
    expect(previewStillCovers(picture, 800, { t0: 0, t1: 1 }, 800)).toBe(true)
    expect(previewStillCovers(picture, 800, { t0: 0, t1: 1 }, 900)).toBe(false)
    expect(previewStillCovers(picture, 800, { t0: 0.2, t1: 0.4 }, 800)).toBe(false)
    expect(previewStillCovers({ ...picture, indices: new Uint8Array() }, 800, { t0: 0, t1: 1 }, 800)).toBe(false)
  })

  it("解析の進み具合と .frq と録音リストを読む", () => {
    expect(parseHostEvent({ type: "status", message: "解析しています　1 / 3 ファイル" })).toEqual({
      type: "status",
      message: "解析しています　1 / 3 ファイル",
    })
    expect(parseHostEvent({
      type: "frqView",
      missing: false,
      name: "あ",
      hop: 256,
      sampleRate: 44100,
      frequency: [440, 0],
    })).toMatchObject({ type: "frqView", frequency: [440, 0], error: null })
    expect(parseHostEvent({ type: "projectPlace", path: "C:/voice" })).toEqual({
      type: "projectPlace",
      path: "C:/voice",
    })
    expect(parseHostEvent({ type: "opened", name: "あ.wav", missing: true, file: 3 })).toEqual({
      type: "opened",
      name: "あ.wav",
      missing: true,
      file: 3,
    })
    expect(parseHostEvent({
      type: "bank",
      generation: 2,
      name: "音",
      encoding: "utf-8",
      profile: [{ label: "年齢", value: "16" }],
      locales: [{ code: "ja", name: "音" }],
    })).toMatchObject({ type: "bank", generation: 2, name: "音", encoding: "utf-8" })
    expect(parseHostEvent({
      type: "effects",
      preset: "標準",
      presets: ["標準"],
      gains: [0, 0, 0, 0, 0, 0, 0, 0],
      noiseNote: "ノイズサプレッサーは、選んだ 0.50秒の無音で抑えます。",
      noiseMessage: "もう少し長く選んでください。",
    })).toMatchObject({ type: "effects", preset: "標準", noiseNote: "ノイズサプレッサーは、選んだ 0.50秒の無音で抑えます。", noiseMessage: "もう少し長く選んでください。" })
    expect(parseHostEvent({
      type: "noiseWave",
      file: "あ.wav",
      lo: [0, -0.2],
      hi: [0.1, 0.4],
      duration: 1.5,
      viewStart: 0.2,
      viewLength: 1,
    })).toEqual({
      type: "noiseWave",
      file: "あ.wav",
      lo: [0, -0.2],
      hi: [0.1, 0.4],
      duration: 1.5,
      viewStart: 0.2,
      viewLength: 1,
    })
    expect(parseHostEvent({ type: "appearance", theme: "dark", accent: "#112233", scale: 1.25, lockPanels: true })).toEqual({
      type: "appearance",
      theme: "dark",
      accent: "#112233",
      scale: 1.3,
      lockPanels: true,
    })
    expect(parseHostEvent({ type: "appearance", theme: "system", accent: null })).toMatchObject({ scale: 1, lockPanels: false })
    expect(parseHostEvent({
      type: "plugins",
      items: [
        { id: "sample", title: "見本", origin: "https://sample.plugins.otoasobi.assets", entry: "index.html" },
        { id: "Bad", title: "外", origin: "https://otoasobi.assets", entry: "../index.html" },
      ],
    })).toEqual({
      type: "plugins",
      items: [{ id: "sample", title: "見本", origin: "https://sample.plugins.otoasobi.assets", entry: "index.html" }],
    })
    expect(parseHostEvent({ type: "plugin", id: "sample", request: "1", ok: false, error: "フォルダを開いていません。" })).toEqual({
      type: "plugin",
      id: "sample",
      request: "1",
      ok: false,
      result: null,
      error: "フォルダを開いていません。",
    })
  })
})

describe("画面の倍率", () => {
  it("50% から 200% の 10% 刻みに収める", () => {
    expect(uiScale(1)).toBe(1)
    expect(uiScale(1.25)).toBe(1.3)
    expect(uiScale(0.1)).toBe(0.5)
    expect(uiScale(4)).toBe(2)
    expect(uiScale("150")).toBe(1)
  })
})
