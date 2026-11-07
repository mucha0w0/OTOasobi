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

// 近い端は一つの境界にする。無音と息だけの境界は出さない。
export function spanBoundaries(spans: PhoneSpan[]): number[] {
  const edges: { time: number; index: number }[] = []
  spans.forEach((span, index) => {
    edges.push({ time: span.start, index })
    edges.push({ time: span.end, index })
  })
  edges.sort((a, b) => a.time - b.time)
  const groups: { time: number; indexes: number[] }[] = []
  for (const edge of edges) {
    const last = groups[groups.length - 1]
    if (last && edge.time - last.time <= 0.0005) {
      last.indexes.push(edge.index)
      continue
    }
    groups.push({ time: edge.time, indexes: [edge.index] })
  }
  return groups
    .filter((group) => group.indexes.some((index) => !isQuietLabel(spans[index]?.label ?? "無音")))
    .map((group) => group.time)
}
