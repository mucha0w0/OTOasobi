import { describe, expect, it } from "vitest"
import {
  absorbOto,
  applyCommands,
  applySnapshot,
  clearsHistory,
  coalesceMs,
  describeEdit,
  emptyHistory,
  emptySnapshot,
  frqStep,
  groupEdits,
  historyLimit,
  pushStep,
  redoHistory,
  textFieldOwnsUndo,
  undoHistory,
  type EditSnapshot,
} from "./history"
import type { EffectsEvent, OtoMark, OtoState, PageCommand } from "./protocol"

function mark(partial: Partial<OtoMark> = {}): OtoMark {
  return {
    alias: "あ",
    offset: 0.1,
    overlap: 0.02,
    preutter: 0.2,
    consonant: 0.3,
    blank: 0.4,
    file: "あ.wav",
    index: 0,
    autoAlias: "あ",
    name: "あ",
    comment: "",
    tuned: [false, false, false, false, false],
    ...partial,
  }
}

function oto(partial: Partial<OtoState> = {}): OtoState {
  const row = mark()
  return {
    type: "oto",
    generation: 1,
    rowGeneration: 0,
    mode: "mono",
    busy: false,
    analyzed: true,
    hasIni: false,
    scope: "all",
    aliasMono: true,
    aliasMonoLocked: false,
    aliasRenzoku: false,
    aliasCvvc: false,
    presets: ["初期値"],
    cvvcPresets: ["初期値"],
    saves: [],
    standalonePreset: "初期値",
    standaloneDirty: false,
    restPreset: "初期値",
    leadPreset: "初期値",
    cvvcPreset: "初期値",
    cvvcDirty: false,
    offset: 0,
    consonant: 0.2,
    blank: 0,
    overlap: { k: 0.5 },
    renzokuOffset: 0,
    renzokuConsonant: 0.2,
    renzokuBlank: 0,
    renzokuOverlap: 0.5,
    cvvcConsonant: 0.2,
    cvvcBlank: 0,
    cvvcOverlap: { k: 0.5 },
    vcOffset: 0,
    vcOverlap: 0.5,
    vcConsonant: { k: 0.5 },
    vcBlank: { k: 0.5 },
    kinds: [],
    marks: [row],
    rows: [row],
    spans: [{ start: 0, end: 0.2, label: "あ", token: "あ" }],
    ...partial,
  }
}

function effects(partial: Partial<EffectsEvent> = {}): EffectsEvent {
  return {
    type: "effects",
    preset: "標準",
    presets: ["標準"],
    noiseEnabled: false,
    noiseReduction: 6,
    noiseFiles: [],
    noiseFile: "",
    noiseStart: 0,
    noiseEnd: 0,
    noiseReady: false,
    noiseMessage: null,
    highpassEnabled: false,
    highpassHz: 100,
    highpassSlope: 12,
    eqEnabled: false,
    gains: [0, 0, 0, 0, 0, 0, 0, 0],
    compressorEnabled: false,
    compressorRatio: 2,
    compressorReduction: 0,
    normalizeEnabled: false,
    normalizeTarget: -3,
    noiseNote: null,
    order: ["noise-suppressor", "highpass", "equalizer", "compressor", "normalize"],
    ...partial,
  }
}

function snap(partial: Partial<EditSnapshot> = {}): EditSnapshot {
  const base = oto()
  return {
    ...emptySnapshot(),
    oto: base,
    marks: base.marks ?? [],
    spans: base.spans ?? [],
    effects: effects(),
    ...partial,
  }
}

function stage(history: ReturnType<typeof emptyHistory>, snapshot: EditSnapshot, commands: PageCommand[], at: number) {
  const edits = []
  let current = snapshot
  for (const command of commands) {
    const edit = describeEdit(command, current)
    if (!edit)
      continue
    edits.push(edit)
    current = applySnapshot(current, command)
  }
  const step = groupEdits(edits, at)
  return {
    history: step ? pushStep(history, step) : history,
    snapshot: current,
    step,
  }
}

describe("履歴の順", () => {
  it("パネルをまたいだ操作は、後からした順に戻る", () => {
    let history = emptyHistory()
    let snapshot = snap()
    ;({ history, snapshot } = stage(history, snapshot, [{ type: "oto", op: "slider", group: "mono", key: "offset", value: 0.05 }], 0))
    ;({ history, snapshot } = stage(history, snapshot, [{ type: "monitor", on: true }], 1000))
    ;({ history, snapshot } = stage(history, snapshot, [{ type: "oto", op: "slider", group: "mono", key: "blank", value: 0.02 }], 2000))
    const first = undoHistory(history)
    const second = first && undoHistory(first.history)
    const third = second && undoHistory(second.history)
    expect(first?.step.undo[0]).toMatchObject({ type: "oto", op: "slider", key: "blank", value: 0 })
    expect(second?.step.undo[0]).toMatchObject({ type: "monitor", on: false })
    expect(third?.step.undo[0]).toMatchObject({ type: "oto", op: "slider", key: "offset", value: 0 })
    expect(undoHistory(third!.history)).toBeNull()
  })

  it("新しい操作でやり直しを捨てる", () => {
    let history = emptyHistory()
    let snapshot = snap()
    ;({ history, snapshot } = stage(history, snapshot, [{ type: "monitor", on: true }], 0))
    const undone = undoHistory(history)!
    const again = stage(undone.history, applyCommands(snapshot, undone.step.undo), [{ type: "monitor", on: true }], 10)
    expect(again.history.redo).toEqual([])
    expect(again.history.undo).toHaveLength(1)
  })

  it("やり直しは戻した手を逆に積む", () => {
    const started = stage(emptyHistory(), snap(), [{ type: "monitor", on: true }], 0)
    const undone = undoHistory(started.history)!
    const redone = redoHistory(undone.history)!
    expect(redone.step.redo[0]).toMatchObject({ type: "monitor", on: true })
    expect(redone.history.undo).toHaveLength(1)
    expect(redone.history.redo).toEqual([])
  })
})

describe("同じ手へのまとめ", () => {
  it("スライダーのドラッグは最初の値から最後の値までを1手にする", () => {
    let history = emptyHistory()
    let snapshot = snap()
    ;({ history, snapshot } = stage(history, snapshot, [{ type: "oto", op: "slider", group: "mono", key: "offset", value: 0.01 }], 0))
    ;({ history, snapshot } = stage(history, snapshot, [{ type: "oto", op: "slider", group: "mono", key: "offset", value: 0.04 }], coalesceMs))
    expect(history.undo).toHaveLength(1)
    expect(history.undo[0]?.undo[0]).toMatchObject({ value: 0 })
    expect(history.undo[0]?.redo[0]).toMatchObject({ value: 0.04 })
    expect(snapshot.oto?.offset).toBe(0.04)
  })

  it("間が空いたら別の手になる", () => {
    let history = emptyHistory()
    let snapshot = snap()
    ;({ history, snapshot } = stage(history, snapshot, [{ type: "oto", op: "slider", group: "mono", key: "offset", value: 0.01 }], 0))
    ;({ history } = stage(history, snapshot, [{ type: "oto", op: "slider", group: "mono", key: "offset", value: 0.04 }], coalesceMs + 1))
    expect(history.undo).toHaveLength(2)
  })

  it("数値表の貼り付けは1手で、セルは後から戻す", () => {
    const row = mark()
    const snapshot = snap({ oto: oto({ rows: [row, mark({ index: 1, alias: "い", name: "い" })] }) })
    const pasted = stage(emptyHistory(), snapshot, [
      { type: "oto", op: "fields", file: "あ.wav", index: 0, alias: "あ", offset: 0.5, overlap: 0.02, preutter: 0.2, consonant: 0.3, blank: 0.4 },
      { type: "oto", op: "comment", file: "あ.wav", index: 1, alias: "い", comment: "次" },
    ], 0)
    expect(pasted.history.undo).toHaveLength(1)
    expect(pasted.step?.undo.map((command) => command.type === "oto" ? command.op : "")).toEqual(["comment", "fields"])
    expect(pasted.step?.undo[0]).toMatchObject({ comment: "" })
    expect(pasted.step?.undo[1]).toMatchObject({ offset: 100, overlap: -80, preutter: 100, consonant: 200, blank: -300 })
    const undone = undoHistory(pasted.history)!
    const restored = applyCommands(pasted.snapshot, undone.step.undo)
    expect(restored.oto?.rows?.[0]?.offset).toBeCloseTo(0.1)
    expect(restored.oto?.rows?.[1]?.comment).toBe("")
  })

  it("数値表の行は mode が ini でも空の操作と照合する", () => {
    const row = mark({ mode: "ini" })
    const snapshot = snap({ oto: oto({ rows: [row] }) })
    const edit = describeEdit({
      type: "oto",
      op: "fields",
      file: "あ.wav",
      index: 0,
      alias: "あ",
      mode: "",
      offset: 250,
      overlap: -80,
      preutter: 100,
      consonant: 200,
      blank: -300,
    }, snapshot)
    expect(edit?.undo[0]).toMatchObject({ op: "fields", offset: 100 })
    const renamed = describeEdit({ type: "oto", op: "aliasName", file: "あ.wav", index: 0, alias: "あ", mode: "", name: "か" }, snapshot)
    expect(renamed?.undo[0]).toMatchObject({ alias: "か", name: "あ" })
    expect(renamed?.redo[0]).toMatchObject({ alias: "あ", name: "か" })
  })
})

describe("対象外", () => {
  it("ファイルを書く操作と、配置や選択は積まない", () => {
    const snapshot = snap()
    const skipped: PageCommand[] = [
      { type: "oto", op: "write" },
      { type: "oto", op: "savePreset", which: "mono", name: "保存" },
      { type: "oto", op: "analyze", force: true },
      { type: "oto", op: "analyzeFile", names: ["あ.wav"] },
      { type: "frq", op: "write", mode: "all", clear: false },
      { type: "frq", op: "stroke", points: [] },
      { type: "frq", op: "octave", frames: [0], octaves: 1 },
      { type: "frq", op: "clear", frames: [0] },
      { type: "effects", op: "apply" },
      { type: "record", name: "あ" },
      { type: "recordStop" },
      { type: "bankPick", which: "icon" },
      { type: "selectFile", name: "あ.wav" },
      { type: "layout", root: null },
      { type: "preview", t0: 0, t1: 1, columns: 32, rows: 32 },
      { type: "guide", note: "C4", semitones: 0, speed: 1 },
      { type: "saveSettings", name: "名", replace: false },
    ]
    for (const command of skipped)
      expect(describeEdit(command, snapshot)).toBeNull()
  })

  it("フォルダの中身が入れ替わる操作は履歴を空にする印になる", () => {
    expect(clearsHistory({ type: "loadSettings", name: "名" })).toBe(true)
    expect(clearsHistory({ type: "effects", op: "apply" })).toBe(true)
    expect(clearsHistory({ type: "oto", op: "analyze", force: true })).toBe(true)
    expect(clearsHistory({ type: "oto", op: "analyzeFile", names: [] })).toBe(true)
    expect(clearsHistory({ type: "oto", op: "write" })).toBe(false)
  })
})

describe("戻すコマンド", () => {
  it("単独音のプリセットは、名前のあとに元のスライダーを戻す", () => {
    const edit = describeEdit({ type: "oto", op: "preset", target: "standalone", name: "やわらか" }, snap())
    expect(edit?.undo[0]).toMatchObject({ op: "preset", name: "初期値" })
    expect(edit?.undo.slice(1)).toEqual(expect.arrayContaining([
      expect.objectContaining({ op: "slider", group: "mono", key: "offset", value: 0 }),
      expect.objectContaining({ op: "slider", group: "monoOverlap", key: "k", value: 0.5 }),
    ]))
  })

  it("先頭音素のプリセットは、連続音と CVVC を一緒に戻す", () => {
    const edit = describeEdit({ type: "oto", op: "preset", target: "rest", name: "別" }, snap())
    expect(edit?.undo).toEqual([
      { type: "oto", op: "preset", target: "rest", name: "初期値" },
      { type: "oto", op: "preset", target: "lead", name: "初期値" },
    ])
  })

  it("エフェクトのプリセットは、適用前の数値を set で戻す", () => {
    const edit = describeEdit({ type: "effects", op: "preset", name: "強め" }, snap())
    expect(edit?.undo[0]).toMatchObject({ op: "set", noiseReduction: 6 })
    expect(edit?.redo[0]).toMatchObject({ op: "preset", name: "強め" })
  })

  it("無音の原音を選ぶと、範囲なしで戻す", () => {
    const edit = describeEdit({ type: "effects", op: "noise", file: "あ.wav" }, snap())
    expect(edit?.undo[0]).toEqual({ type: "effects", op: "noise", file: "" })
  })

  it("無音の範囲は、選ぶ前の範囲なしに戻す", () => {
    const edit = describeEdit(
      { type: "effects", op: "noise", file: "あ.wav", start: 0.2, end: 0.8 },
      snap({ effects: effects({ noiseFile: "あ.wav", noiseReady: false }) }),
    )
    expect(edit?.undo[0]).toEqual({ type: "effects", op: "noise", file: "あ.wav" })
  })
})

describe("上限と文字欄", () => {
  it("古い手から捨てて 100 手までにする", () => {
    let history = emptyHistory()
    let snapshot = snap()
    for (let i = 0; i < historyLimit + 5; i++) {
      const turned = stage(history, snapshot, [{ type: "monitor", on: i % 2 === 0 }], i * 1000)
      history = turned.history
      snapshot = turned.snapshot
    }
    expect(history.undo).toHaveLength(historyLimit)
    expect(history.undo[0]?.at).toBe(5 * 1000)
  })

  it("文字が最後に積んだ値と違うあいだは欄が取り消す", () => {
    expect(textFieldOwnsUndo(true, "あいう", "あい")).toBe(true)
    expect(textFieldOwnsUndo(true, "あい", "あい")).toBe(false)
    expect(textFieldOwnsUndo(true, "あい", null)).toBe(false)
    expect(textFieldOwnsUndo(false, "あ", "い")).toBe(false)
  })
})

describe("frq のラジオ", () => {
  it("生成ボタンではなく、対象と先行発声の変更を1手にする", () => {
    const step = frqStep({ mode: "all", preutter: "keep" }, { mode: "missing", preutter: "keep" }, 0)
    expect(step?.key).toBe("frq.mode")
    expect(step?.undo).toEqual([])
    expect(step?.undoFrq).toEqual({ mode: "all", preutter: "keep" })
    const history = pushStep(emptyHistory(), step!)
    expect(undoHistory(history)?.step.undoFrq?.mode).toBe("all")
  })
})

describe("ホストから届いた値", () => {
  it("行の無い原音設定は、持っている表を残す", () => {
    const current = snap()
    const next = absorbOto(current, oto({ rows: null, offset: 0.03, marks: [mark({ offset: 0.2 })] }))
    expect(next.oto?.rows).toHaveLength(1)
    expect(next.oto?.offset).toBe(0.03)
    expect(next.marks[0]?.offset).toBe(0.2)
  })
})
