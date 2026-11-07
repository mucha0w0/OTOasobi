import { describe, expect, it } from "vitest"
import { contains, createDefault, hidePane, showPane } from "./layout"

describe("初期配置", () => {
  const root = createDefault()

  it("製品の初期パネルだけを開く", () => {
    for (const id of ["file", "wave", "spec", "loud", "otoBand", "oto", "otoIni"])
      expect(contains(root, id)).toBe(true)
    expect(contains(root, "rec")).toBe(false)
    expect(contains(root, "mic")).toBe(false)
  })

  it("隠すと残り、足すと既存の葉を割る", () => {
    const hidden = hidePane(root, "otoIni")
    expect(contains(hidden, "otoIni")).toBe(false)
    expect(contains(hidden, "oto")).toBe(true)
    const shown = showPane(hidden, "mic", { oto: { width: 400, height: 200 } })
    expect(contains(shown, "mic")).toBe(true)
    expect(contains(shown, "file")).toBe(true)
  })
})
