import { describe, expect, it } from "vitest"
import { noticeTone } from "./notice"

describe("通知の種類", () => {
  it("失敗、成功、情報を分ける", () => {
    expect(noticeTone("同じ名前の txt がありません。")).toBe("error")
    expect(noticeTone("保存に失敗しました。")).toBe("error")
    expect(noticeTone("ホストに接続できません。")).toBe("error")
    expect(noticeTone("設定を保存しました。")).toBe("success")
    expect(noticeTone("3 件の .frq を書きました。")).toBe("success")
    expect(noticeTone("解析しています　1 / 3 ファイル")).toBe("info")
  })
})