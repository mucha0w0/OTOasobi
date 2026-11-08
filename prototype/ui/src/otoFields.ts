import type { OtoMark } from "./protocol"

export function round3(value: number): number {
  const sign = value < 0 ? -1 : 1
  const scaled = Math.abs(value) * 1000
  const floor = Math.floor(scaled)
  const diff = scaled - floor
  const even = floor % 2 === 0
  const rounded = diff > 0.5 || (diff === 0.5 && !even) ? floor + 1 : floor
  return (sign * rounded) / 1000
}

export function otoField(mark: OtoMark, point: number): number {
  const seconds = point === 0
    ? mark.offset
    : point === 1
      ? mark.overlap - mark.offset
      : point === 2
        ? mark.preutter - mark.offset
        : point === 3
          ? mark.consonant - mark.offset
          : mark.offset - mark.blank
  return round3(seconds * 1000)
}
