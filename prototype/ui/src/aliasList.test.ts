import { describe, expect, it } from "vitest"
import { aliasId, aliasPanelMode, aliasStepIndex, currentAliasId, findAlias, markIndexForAlias, pendingAliasReady, selectedAliasIds } from "./aliasList"
import type { OtoMark } from "./protocol"

function mark(over: Partial<OtoMark> = {}): OtoMark {
  return {
    alias: "あ",
    offset: 0,
    overlap: 0,
    preutter: 0,
    consonant: 0,
    blank: 0,
    file: "あ.wav",
    index: 0,
    autoAlias: "あ",
    name: "",
    comment: "",
    tuned: [],
    mode: "renzoku",
    ...over,
  }
}

describe("エイリアスリスト", () => {
  const rows = [
    mark(),
    mark({ alias: "a い", autoAlias: "a い", index: 1, file: "あい.wav" }),
    mark({ alias: "い", autoAlias: "い", index: 0, file: "あい.wav", mode: "mono" }),
  ]

  it("ファイルの選択と同じ行を選び、強調中だけを現在にする", () => {
    expect(selectedAliasIds(rows, ["あい.wav"])).toEqual([
      aliasId(rows[1]!),
      aliasId(rows[2]!),
    ])
    expect(currentAliasId(rows, "あい.wav", 0, "mono")).toBe(aliasId(rows[2]!))
    expect(currentAliasId(rows, "あい.wav", 0, "renzoku")).toBe("")
    expect(currentAliasId(rows, "あい.wav", 1, "renzoku")).toBe(aliasId(rows[1]!))
    expect(currentAliasId(rows, "あい.wav", -1, "renzoku")).toBe("")
  })

  it("今のモードの行を優先して強調する", () => {
    const same = [
      mark({ alias: "あ", index: 0, mode: "mono" }),
      mark({ alias: "- あ", autoAlias: "- あ", index: 0, mode: "renzoku" }),
    ]
    expect(currentAliasId(same, "あ.wav", 0, "renzoku")).toBe(aliasId(same[1]!))
    expect(currentAliasId(same, "あ.wav", 0, "mono")).toBe(aliasId(same[0]!))
  })

  it("id から行を戻し、グラフの線に合わせる", () => {
    expect(findAlias(rows, aliasId(rows[1]!))).toEqual(rows[1])
    const marks = [
      mark({ alias: "a い *", autoAlias: "a い", index: 1, file: "あい.wav" }),
    ]
    expect(markIndexForAlias(rows[1]!, marks, "renzoku")).toBe(1)
    expect(markIndexForAlias(rows[2]!, marks, "renzoku")).toBe(-1)
    expect(markIndexForAlias({ ...rows[2]!, autoAlias: "a い" }, marks, "renzoku")).toBe(1)
  })

  it("選んだ行のモードへタブを合わせ、届くまで強調を待つ", () => {
    expect(aliasPanelMode("cvvc")).toBe("cvvc")
    expect(aliasPanelMode("other")).toBeNull()
    expect(pendingAliasReady({ file: "あ.wav", mode: "cvvc" }, "あ.wav", "mono")).toBe(false)
    expect(pendingAliasReady({ file: "あ.wav", mode: "cvvc" }, "あ.wav", "cvvc")).toBe(true)
    expect(pendingAliasReady({ file: "あ.wav" }, "あ.wav", "mono")).toBe(true)
  })

  it("ホイールは強調中のエイリアスから進み、無ければそのファイルの先頭から", () => {
    expect(aliasStepIndex(rows, "あい.wav", 1, "renzoku")).toBe(1)
    expect(aliasStepIndex(rows, "あい.wav", -1, "renzoku")).toBe(1)
    expect(aliasStepIndex(rows, "あ.wav", -1, "mono")).toBe(0)
    expect(aliasStepIndex(rows, "ない.wav", -1, "mono")).toBe(-1)
  })
})
