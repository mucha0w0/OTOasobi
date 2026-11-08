import { describe, expect, it } from "vitest"
import type { LayoutNode } from "./layout"
import { measureFloors, measureSplitters, minPane, moveSplitter, pathKey } from "./splitter"

const width = 1000
const height = 800

describe("境界のドラッグ", () => {
  it("掴んだ境界以外は押した位置に残る", () => {
    const root = v(0.75, v(400 / 600, v(0.5, leaf("a"), leaf("b")), leaf("c")), leaf("d"))
    const frozen = measureSplitters(root, width, height)
    expect(frozen.get("0.0")).toBeCloseTo(200, 1)
    expect(frozen.get("0")).toBeCloseTo(400, 1)
    expect(frozen.get("")).toBeCloseTo(600, 1)

    const next = move(root, [0], 420, frozen)
    const now = measureSplitters(next, width, height)
    expect(now.get("0")).toBeCloseTo(420, 0)
    expect(now.get("0.0")).toBeCloseTo(200, 0)
    expect(now.get("")).toBeCloseTo(600, 0)
  })

  it("ほかの境界が動く手前で止まる", () => {
    const root = v(0.75, v(400 / 600, v(0.5, leaf("a"), leaf("b")), leaf("c")), leaf("d"))
    const frozen = measureSplitters(root, width, height)

    const upward = move(root, [0], 100, frozen)
    const up = measureSplitters(upward, width, height)
    expect(up.get("0")).toBeCloseTo(360, 0)
    expect(up.get("0.0")).toBeCloseTo(200, 0)
    expect(up.get("")).toBeCloseTo(600, 0)

    const downward = move(upward, [0], 900, frozen)
    const down = measureSplitters(downward, width, height)
    expect(down.get("0")).toBeCloseTo(440, 0)
    expect(down.get("0.0")).toBeCloseTo(200, 0)
    expect(down.get("")).toBeCloseTo(600, 0)
  })

  it("ほかの境界が動く区間を飛び越さない", () => {
    const root = v(0.75, v(400 / 600, v(0.5, leaf("a"), leaf("b")), leaf("c")), leaf("d"))
    const frozen = measureSplitters(root, width, height)
    const next = move(root, [0], 250, frozen)
    const now = measureSplitters(next, width, height)
    expect(now.get("0")).toBeCloseTo(360, 0)
    expect(now.get("0.0")).toBeCloseTo(200, 0)
  })

  it("下の入れ子は上の境界を動かしても残る", () => {
    const root = v(0.25, leaf("a"), v(0.5, leaf("b"), v(0.5, leaf("c"), leaf("d"))))
    const frozen = measureSplitters(root, width, height)
    expect(frozen.get("")).toBeCloseTo(200, 1)
    expect(frozen.get("1")).toBeCloseTo(500, 1)
    expect(frozen.get("1.1")).toBeCloseTo(650, 1)

    const near = move(root, [1], 530, frozen)
    const nearAt = measureSplitters(near, width, height)
    expect(nearAt.get("1")).toBeCloseTo(530, 0)
    expect(nearAt.get("")).toBeCloseTo(200, 0)
    expect(nearAt.get("1.1")).toBeCloseTo(650, 0)

    const far = move(root, [1], 100, frozen)
    const farAt = measureSplitters(far, width, height)
    expect(farAt.get("1")).toBeCloseTo(360, 0)
    expect(farAt.get("")).toBeCloseTo(200, 0)
    expect(farAt.get("1.1")).toBeCloseTo(650, 0)
  })

  it("左右のドラッグでは上下の境界と入れ子の列が残る", () => {
    const root = h(0.5, v(0.25, leaf("a"), leaf("b")), h(0.5, leaf("c"), leaf("d")))
    const frozen = measureSplitters(root, width, height)
    expect(frozen.get("")).toBeCloseTo(500, 1)
    expect(frozen.get("0")).toBeCloseTo(200, 1)
    expect(frozen.get("1")).toBeCloseTo(750, 1)

    const vertical = move(root, [0], 280, frozen)
    const verticalAt = measureSplitters(vertical, width, height)
    expect(verticalAt.get("0")).toBeCloseTo(280, 0)
    expect(verticalAt.get("")).toBeCloseTo(500, 0)
    expect(verticalAt.get("1")).toBeCloseTo(750, 0)

    const again = measureSplitters(vertical, width, height)
    const horizontal = move(vertical, [], 700, again)
    const horizontalAt = measureSplitters(horizontal, width, height)
    expect(horizontalAt.get("")).toBeCloseTo(590, 0)
    expect(horizontalAt.get("0")).toBeCloseTo(280, 0)
    expect(horizontalAt.get("1")).toBeCloseTo(750, 0)
  })

  it("押し始めの位置を続けて使う", () => {
    let root = v(0.75, v(400 / 600, v(0.5, leaf("a"), leaf("b")), leaf("c")), leaf("d"))
    const frozen = measureSplitters(root, width, height)
    root = move(root, [0], 410, frozen)
    root = move(root, [0], 430, frozen)
    root = move(root, [0], 200, frozen)
    const now = measureSplitters(root, width, height)
    expect(now.get("0")).toBeCloseTo(360, 0)
    expect(now.get("0.0")).toBeCloseTo(200, 0)
    expect(now.get("")).toBeCloseTo(600, 0)
  })

  it("初期配置の段では掴んだ境界以外が動かない", () => {
    const loud = v(0.368, leaf("loud"), leaf("band"))
    const spec = v(0.457, leaf("spec"), loud)
    const wave = v(0.3, leaf("wave"), spec)
    const graphs = h(0.746, wave, leaf("oto"))
    let root = h(0.182, leaf("file"), graphs)
    const wide = 1400
    const tall = 720
    const frozen = measureSplitters(root, wide, tall)
    const floors = measureFloors(root, wide, tall)
    const specPath = [1, 0, 1]

    root = moveSplitter(root, specPath, frozen.get(pathKey(specPath))! + 36, frozen, wide, tall, floors)
    const nudged = measureSplitters(root, wide, tall)
    expect(nudged.get(pathKey(specPath))).toBeCloseTo(frozen.get(pathKey(specPath))! + 36, 0)
    sameExcept(frozen, nudged, specPath)

    root = moveSplitter(root, specPath, 0, frozen, wide, tall, floors)
    const up = measureSplitters(root, wide, tall)
    sameExcept(frozen, up, specPath)
    expect(up.get(pathKey(specPath))!).toBeLessThan(frozen.get(pathKey(specPath))! - 1)

    root = moveSplitter(root, specPath, tall, frozen, wide, tall, floors)
    const down = measureSplitters(root, wide, tall)
    sameExcept(frozen, down, specPath)
    expect(down.get(pathKey(specPath))!).toBeGreaterThan(frozen.get(pathKey(specPath))! + 1)
  })

  it("短い段を広げるとき、遠い面が下限に当たるまで進む", () => {
    const root = v(160 / 900, leaf("wave"), v(200 / 740, v(0.25, leaf("loud"), leaf("spec")), leaf("band")))
    const tall = 900
    const frozen = measureSplitters(root, width, tall)
    expect(frozen.get("")).toBeCloseTo(160, 0)
    expect(frozen.get("1")).toBeCloseTo(360, 0)
    expect(frozen.get("1.0")).toBeCloseTo(210, 0)

    const next = moveSplitter(root, [1], tall, frozen, width, tall)
    const now = measureSplitters(next, width, tall)
    expect(now.get("1")).toBeCloseTo(tall - minPane, 0)
    expect(now.get("1.0")).toBeCloseTo(210, 0)
    expect(now.get("")).toBeCloseTo(160, 0)
  })
})

function move(root: LayoutNode, path: number[], proposed: number, frozen: ReadonlyMap<string, number>) {
  return moveSplitter(root, path, proposed, frozen, width, height)
}

function sameExcept(before: Map<string, number>, after: Map<string, number>, dragged: number[]) {
  expect(after.size).toBe(before.size)
  const skip = pathKey(dragged)
  for (const [key, value] of before) {
    if (key === skip)
      continue
    expect(after.get(key)).toBeCloseTo(value, 0)
  }
}

function leaf(id: string): LayoutNode {
  return { id }
}

function h(ratio: number, first: LayoutNode, second: LayoutNode): LayoutNode {
  return { axis: "h", ratio, first, second }
}

function v(ratio: number, first: LayoutNode, second: LayoutNode): LayoutNode {
  return { axis: "v", ratio, first, second }
}
