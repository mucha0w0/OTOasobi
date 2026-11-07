export type TitleEdge = "left" | "right" | "top"

const slopX = 12
const slopY = 16

/** 見出しの、分割に接する側。ここはパネル移動ではなくリサイズにする。 */
export function pickTitleResize(
  x: number,
  y: number,
  width: number,
  available: { left: boolean; right: boolean; top: boolean },
): TitleEdge | null {
  const near: { edge: TitleEdge; score: number }[] = []
  if (available.left && x >= 0 && x <= slopX)
    near.push({ edge: "left", score: x / slopX })
  if (available.right && width - x >= 0 && width - x <= slopX)
    near.push({ edge: "right", score: (width - x) / slopX })
  if (available.top && y >= 0 && y <= slopY)
    near.push({ edge: "top", score: y / slopY })
  near.sort((a, b) => a.score - b.score)
  return near[0]?.edge ?? null
}
