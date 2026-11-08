import { describe, expect, it } from "vitest"
import type { OtoMark } from "./protocol"
import { cellText, commitCommands, copyText, normalizeRect, pasteCommands, tabCell } from "./otoSheet"

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

describe("oto数値表", () => {
  const left = mark({})
  const right = mark({
    file: "い.wav",
    index: 1,
    alias: "い",
    autoAlias: "い",
    offset: 0.1,
    overlap: 0.12,
    preutter: 0.2,
    consonant: 0.3,
    blank: 0.5,
    comment: "次",
  })

  it("コピーは番号付きのエイリアスで、編集印は入れない", () => {
    const tuned = mark({ alias: "あ2", autoAlias: "あ", tuned: [true, false, false, false, false] })
    const text = copyText([tuned], normalizeRect({ row: 0, col: 0 }, { row: 0, col: 3 }))
    expect(text).toBe("あ.wav\tあ2\t200.000\t50.000")
    expect(cellText(tuned, 1)).toBe("あ2")
  })

  it("数値の貼り付けは、その欄だけをミリ秒で書き、0 未満のオフセットは 0 にする", () => {
    const one = pasteCommands([left], { row: 0, col: 2 }, normalizeRect({ row: 0, col: 2 }, { row: 0, col: 2 }), "-5")
    expect(one).toEqual([{
      type: "oto",
      op: "fields",
      file: "あ.wav",
      index: 0,
      alias: "あ",
      mode: "",
      offset: 0,
      overlap: 50,
      preutter: 100,
      consonant: 200,
      blank: -600,
    }])
    const overlap = pasteCommands([left], { row: 0, col: 3 }, normalizeRect({ row: 0, col: 3 }, { row: 0, col: 3 }), "-12,5")
    expect(overlap[0]).toMatchObject({ op: "fields", mode: "", overlap: -12.5, offset: 200 })
  })

  it("1 マスを範囲へ貼ると、その範囲を同じ値で埋める", () => {
    const commands = pasteCommands(
      [left, right],
      { row: 0, col: 7 },
      normalizeRect({ row: 0, col: 7 }, { row: 1, col: 7 }),
      "メモ",
    )
    expect(commands).toEqual([
      { type: "oto", op: "comment", file: "あ.wav", index: 0, alias: "あ", mode: "", comment: "メモ" },
      { type: "oto", op: "comment", file: "い.wav", index: 1, alias: "い", mode: "", comment: "メモ" },
    ])
  })

  it("名前と 5 つの数値がある行は、数値の列へ貼ると 5 欄へ入る", () => {
    const commands = pasteCommands(
      [left],
      { row: 0, col: 2 },
      normalizeRect({ row: 0, col: 2 }, { row: 0, col: 2 }),
      "か\t10\t-3\t20\t30\t-40",
    )
    expect(commands).toEqual([{
      type: "oto",
      op: "fields",
      file: "あ.wav",
      index: 0,
      alias: "あ",
      mode: "",
      offset: 10,
      overlap: -3,
      preutter: 20,
      consonant: 30,
      blank: -40,
    }])
  })

  it("エイリアスから貼ると隣の欄へ進み、ファイル名は変わらない", () => {
    const commands = pasteCommands(
      [left, right],
      { row: 0, col: 0 },
      normalizeRect({ row: 0, col: 0 }, { row: 0, col: 0 }),
      "あ.wav\tか\t10\nい.wav\tき\t",
    )
    expect(commands).toEqual([
      {
        type: "oto",
        op: "fields",
        file: "あ.wav",
        index: 0,
        alias: "あ",
        mode: "",
        offset: 10,
        overlap: 50,
        preutter: 100,
        consonant: 200,
        blank: -600,
      },
      { type: "oto", op: "aliasName", file: "あ.wav", index: 0, alias: "あ", mode: "", name: "か" },
      { type: "oto", op: "aliasName", file: "い.wav", index: 1, alias: "い", mode: "", name: "き" },
    ])
  })

  it("同じ値と、区切りの入ったエイリアスは送らない", () => {
    expect(commitCommands([left], { row: 0, col: 2 }, "200")).toEqual([])
    expect(commitCommands([left], { row: 0, col: 1 }, "あ *")).toEqual([])
    expect(commitCommands([mark({ name: "い" })], { row: 0, col: 1 }, "あ,い")).toEqual([])
  })

  it("Tab は行末で次の行へ進む", () => {
    expect(tabCell({ row: 0, col: 7 }, false, 2)).toEqual({ row: 1, col: 0 })
    expect(tabCell({ row: 1, col: 7 }, false, 2)).toEqual({ row: 1, col: 7 })
  })
})
