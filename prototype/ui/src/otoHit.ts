import { timeAt, xAt, type OtoMark } from "./protocol"
import { isQuietLabel, isVowelLabel, type PhoneSpan } from "./spans"

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

// 線のないところをクリックしたとき、重なりは次の音素へ。音素が無いときは外す。
export function cycleOtoFocus(
  marks: OtoMark[],
  x: number,
  width: number,
  t0: number,
  t1: number,
  focus: number,
): number {
  const hits = otoHitsAt(marks, x, width, t0, t1)
  if (hits.length === 0)
    return -1
  const at = hits.findIndex((mark) => mark.index === focus)
  if (at >= 0)
    return hits.length === 1 ? focus : hits[(at + 1) % hits.length]!.index
  return bestOtoHit(hits, x, width, t0, t1).index
}

// 強調中の母音は、別の行になっていない直前の子音も含む。
export function focusedOwnsTime(marks: OtoMark[], spans: PhoneSpan[], focus: number, time: number): boolean {
  const anchor = anchorSpan(marks, spans, focus)
  if (anchor < 0)
    return false
  return spans.some((span, index) => {
    if (time < span.start - 1e-4 || time > span.end + 1e-4)
      return false
    return ownsSpan(marks, spans, focus, anchor, index)
  })
}

function otoHitsAt(marks: OtoMark[], x: number, width: number, t0: number, t1: number): OtoMark[] {
  const time = timeAt(x, width, t0, t1)
  return marks
    .filter((mark) => contains(mark, time) || pointNear(mark, x, width, t0, t1))
    .sort((a, b) => a.offset - b.offset || a.index - b.index)
}

function bestOtoHit(hits: OtoMark[], x: number, width: number, t0: number, t1: number): OtoMark {
  let best = hits[0]!
  let bestDist = 6
  let found = false
  for (const mark of hits) {
    for (const time of pointTimes(mark)) {
      const dist = Math.abs(xAt(time, width, t0, t1) - x)
      if (dist > bestDist)
        continue
      bestDist = dist
      best = mark
      found = true
    }
  }
  if (found)
    return best
  let bestWidth = Number.POSITIVE_INFINITY
  for (const mark of hits) {
    const span = Math.abs(mark.blank - mark.offset)
    if (span >= bestWidth)
      continue
    bestWidth = span
    best = mark
  }
  return best
}

function contains(mark: OtoMark, time: number): boolean {
  const start = Math.min(mark.offset, mark.blank)
  const end = Math.max(mark.offset, mark.blank)
  return time >= start - 1e-9 && time <= end + 1e-9
}

function pointNear(mark: OtoMark, x: number, width: number, t0: number, t1: number): boolean {
  return pointTimes(mark).some((time) => Math.abs(xAt(time, width, t0, t1) - x) <= 6)
}

function anchorSpan(marks: OtoMark[], spans: PhoneSpan[], focus: number): number {
  const mark = marks.find((item) => item.index === focus)
  if (!mark)
    return -1
  let best = -1
  let bestDelta = Number.POSITIVE_INFINITY
  spans.forEach((span, index) => {
    if (mark.preutter < span.start - 0.002 || mark.preutter > span.end + 0.002)
      return
    const delta = Math.abs(span.start - mark.preutter)
    if (delta >= bestDelta)
      return
    bestDelta = delta
    best = index
  })
  return best
}

function ownsSpan(marks: OtoMark[], spans: PhoneSpan[], focus: number, anchor: number, index: number): boolean {
  if (index === anchor)
    return true
  const vowel = spans[anchor]
  const consonant = spans[index]
  if (!vowel || !consonant || index !== anchor - 1 || anchorsOther(marks, spans, focus, index))
    return false
  if (!isVowelLabel(vowel.label))
    return false
  return consonant.token === vowel.token
    && consonant.token.length > 0
    && !isVowelLabel(consonant.label)
    && !isQuietLabel(consonant.label)
}

function anchorsOther(marks: OtoMark[], spans: PhoneSpan[], focus: number, span: number): boolean {
  return marks.some((mark) => mark.index !== focus && anchorSpan(marks, spans, mark.index) === span)
}
