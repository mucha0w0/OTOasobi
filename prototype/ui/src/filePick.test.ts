import { describe, expect, it } from "vitest"
import { applyFileClick, emptyFilePick, pruneFilePick, type FilePick } from "./filePick"

const names = ["a.wav", "b.wav", "c.wav", "d.wav", "e.wav"]

function click(pick: FilePick, name: string, shift = false, ctrl = false) {
  return applyFileClick(names, pick, name, shift, ctrl)
}

describe("ファイルリストの複数選択", () => {
  it("クリックはそのファイルだけを選び、そこを起点にする", () => {
    const pick = click(emptyFilePick, "c.wav")
    expect(pick).toEqual({ selected: ["c.wav"], active: "c.wav", anchor: "c.wav" })
    expect(click(pick, "a.wav").selected).toEqual(["a.wav"])
  })

  it("Shiftは起点からそこまでを選び、起点は残す", () => {
    const pick = click(click(emptyFilePick, "b.wav"), "d.wav", true)
    expect(pick.selected).toEqual(["b.wav", "c.wav", "d.wav"])
    expect(pick.active).toBe("d.wav")
    expect(pick.anchor).toBe("b.wav")
    expect(click(pick, "a.wav", true).selected).toEqual(["a.wav", "b.wav"])
  })

  it("Ctrlは追加と解除で、最後に足したファイルを表示対象にする", () => {
    const added = click(click(emptyFilePick, "b.wav"), "d.wav", false, true)
    expect(added.selected).toEqual(["b.wav", "d.wav"])
    expect(added.active).toBe("d.wav")
    const removed = click(added, "b.wav", false, true)
    expect(removed.selected).toEqual(["d.wav"])
    expect(removed.active).toBe("d.wav")
  })

  it("表示中のファイルをCtrlで外すと、近い選択を表示する", () => {
    const pick = click(click(click(emptyFilePick, "a.wav"), "c.wav", false, true), "e.wav", false, true)
    const removed = click(pick, "e.wav", false, true)
    expect(removed.selected).toEqual(["a.wav", "c.wav"])
    expect(removed.active).toBe("c.wav")
  })

  it("最後の1件はCtrlでは外さない", () => {
    const pick = click(emptyFilePick, "b.wav")
    expect(click(pick, "b.wav", false, true).selected).toEqual(["b.wav"])
  })

  it("Ctrl+Shiftは範囲を足す", () => {
    const pick = click(click(click(emptyFilePick, "a.wav"), "e.wav", false, true), "c.wav", true, true)
    expect(pick.selected).toEqual(["a.wav", "c.wav", "d.wav", "e.wav"])
    expect(pick.active).toBe("c.wav")
    expect(pick.anchor).toBe("e.wav")
  })

  it("一覧から消えたファイルは選択から外し、開いているファイルは残す", () => {
    const pick = pruneFilePick(["a.wav", "c.wav"], {
      selected: ["a.wav", "b.wav", "c.wav"],
      active: "b.wav",
      anchor: "b.wav",
    })
    expect(pick.selected).toEqual(["a.wav", "c.wav"])
    expect(pick.active).toBe("b.wav")
    expect(pick.anchor).toBe("a.wav")
    expect(pruneFilePick(["a.wav"], { selected: [], active: "a.wav", anchor: "" }).selected).toEqual(["a.wav"])
  })
})
