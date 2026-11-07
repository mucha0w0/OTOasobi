export type PhoneSpan = {
  start: number
  end: number
  label: string
  token: string
}

const vowels = new Set(["a", "i", "u", "e", "o", "N", "M"])

export function isQuietLabel(label: string): boolean {
  return label === "無音" || label === "息"
}

export function isVowelLabel(label: string): boolean {
  return vowels.has(label)
}

export function spanCaption(span: Pick<PhoneSpan, "label" | "token">): string {
  if (!span.token || span.token === "_" || span.token === span.label)
    return span.label
  return `${span.token}  ${span.label}`
}

export function spanFill(label: string, wave: boolean): string | null {
  if (isQuietLabel(label))
    return null
  const alpha = (wave ? 70 : 88) / 255
  const [r, g, b] = vowels.has(label) ? [15, 157, 142] : [217, 119, 6]
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

export type SpanEdge = { index: number; start: boolean }

export type BoundaryGroup = { time: number; edges: SpanEdge[] }

const linkGap = 0.0005
const minSpan = 0.005

// 近い端は一つの境界にする。無音と息だけの境界は出さない。
export function spanBoundaries(spans: PhoneSpan[]): number[] {
  return boundaryGroups(spans)
    .filter((group) => !isQuietBoundary(spans, group))
    .map((group) => group.time)
}

export function boundaryGroups(spans: PhoneSpan[]): BoundaryGroup[] {
  const edges: { time: number; index: number; start: boolean }[] = []
  spans.forEach((span, index) => {
    edges.push({ time: span.start, index, start: true })
    edges.push({ time: span.end, index, start: false })
  })
  edges.sort((a, b) => a.time - b.time)
  const groups: BoundaryGroup[] = []
  for (const edge of edges) {
    const last = groups[groups.length - 1]
    if (last && edge.time - last.time <= linkGap) {
      last.edges.push({ index: edge.index, start: edge.start })
      continue
    }
    groups.push({ time: edge.time, edges: [{ index: edge.index, start: edge.start }] })
  }
  return groups
}

export function isQuietBoundary(spans: PhoneSpan[], group: BoundaryGroup): boolean {
  return group.edges.length > 0 && group.edges.every((edge) => isQuietLabel(spans[edge.index]?.label ?? "無音"))
}

export function hitBoundary(
  spans: PhoneSpan[],
  x: number,
  width: number,
  t0: number,
  t1: number,
  slop = 8,
): number {
  const span = t1 - t0
  if (!(span > 0) || !(width > 0))
    return -1
  let best = -1
  let distance = slop
  boundaryGroups(spans).forEach((group, index) => {
    if (isQuietBoundary(spans, group))
      return
    const gap = Math.abs(((group.time - t0) / span) * width - x)
    if (gap > distance)
      return
    distance = gap
    best = index
  })
  return best
}

// 掴んだ境界を、隣の境界と 5ms を残して動かす。元の区間から計算する。
export function moveBoundary(origin: PhoneSpan[], groupIndex: number, time: number, duration: number): PhoneSpan[] {
  const groups = boundaryGroups(origin)
  const group = groups[groupIndex]
  if (!group || !Number.isFinite(time))
    return origin
  const limits = boundaryLimits(origin, groups, groupIndex, duration)
  const at = Math.min(limits.hi, Math.max(limits.lo, time))
  return origin.map((span, index) => {
    let start = span.start
    let end = span.end
    for (const edge of group.edges) {
      if (edge.index !== index)
        continue
      if (edge.start)
        start = at
      else
        end = at
    }
    return start === span.start && end === span.end ? span : { ...span, start, end }
  })
}

export function spanTimesChanged(origin: PhoneSpan[], next: PhoneSpan[]): boolean {
  return next.some((span, index) => {
    const before = origin[index]
    return !before || Math.abs(span.start - before.start) > 1e-6 || Math.abs(span.end - before.end) > 1e-6
  })
}

function boundaryLimits(
  spans: PhoneSpan[],
  groups: BoundaryGroup[],
  index: number,
  duration: number,
): { lo: number; hi: number } {
  const group = groups[index]
  if (!group || group.edges.length === 0)
    return { lo: 0, hi: 0 }
  const first = group.edges[0]!
  const firstSpan = spans[first.index]
  const time = first.start ? firstSpan?.start ?? 0 : firstSpan?.end ?? 0
  let lo = 0
  let hi = Math.max(duration, 0)
  for (const edge of group.edges) {
    const span = spans[edge.index]
    if (!span)
      continue
    if (edge.start)
      hi = Math.min(hi, span.end - minSpan)
    else
      lo = Math.max(lo, span.start + minSpan)
  }
  groups.forEach((other, otherIndex) => {
    if (otherIndex === index || other.edges.length === 0)
      return
    const edge = other.edges[0]!
    const span = spans[edge.index]
    if (!span)
      return
    const otherTime = edge.start ? span.start : span.end
    if (otherTime < time)
      lo = Math.max(lo, otherTime + minSpan)
    else if (otherTime > time)
      hi = Math.min(hi, otherTime - minSpan)
  })
  if (hi < lo)
    hi = lo
  return { lo, hi }
}
