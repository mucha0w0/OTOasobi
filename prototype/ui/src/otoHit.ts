import { xAt, type OtoMark } from "./protocol"

const pointTimes = (mark: OtoMark) => [mark.offset, mark.overlap, mark.preutter, mark.consonant, mark.blank]

export function hitOtoPoint(
  marks: OtoMark[],
  x: number,
  width: number,
  t0: number,
  t1: number,
  slop = 6,
): { index: number; point: number } | null {
  let best = -1
  let point = -1
  let distance = slop
  marks.forEach((mark, index) => {
    pointTimes(mark).forEach((time, item) => {
      const gap = Math.abs(xAt(time, width, t0, t1) - x)
      if (gap > distance)
        return
      distance = gap
      best = index
      point = item
    })
  })
  return best < 0 ? null : { index: best, point }
}
