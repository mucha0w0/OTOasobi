export const frqRow = 14
export const frqRuler = 20
export const frqUnvoiced = 22
export const frqOctaves = 2

const names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]

export function midiOf(hertz: number): number {
  return 69 + 12 * Math.log2(hertz / 440)
}

export function noteName(midi: number): string {
  const whole = Math.floor(midi)
  const degree = ((whole % 12) + 12) % 12
  return `${names[degree]}${Math.floor(whole / 12) - 1}`
}

export function isBlack(midi: number): boolean {
  const degree = ((Math.floor(midi) % 12) + 12) % 12
  return degree === 1 || degree === 3 || degree === 6 || degree === 8 || degree === 10
}

// 有声の中央から上下 2 オクターブ。中央より外の点は、その高さまで広げる。
// 有声が無いときは C4 を中央にする。
export function pitchSpan(frequency: number[]): { low: number; high: number; center: number } {
  const midis: number[] = []
  for (const hz of frequency) {
    if (hz > 0 && Number.isFinite(hz))
      midis.push(midiOf(hz))
  }
  if (midis.length === 0)
    return { low: 60 - 12 * frqOctaves, high: 60 + 12 * frqOctaves, center: 60 }
  midis.sort((a, b) => a - b)
  const mid = midis.length % 2 === 1
    ? midis[(midis.length - 1) / 2]
    : (midis[midis.length / 2 - 1] + midis[midis.length / 2]) / 2
  let low = Math.floor(mid - 12 * frqOctaves)
  let high = Math.ceil(mid + 12 * frqOctaves)
  low = Math.min(low, Math.floor(midis[0]))
  high = Math.max(high, Math.ceil(midis[midis.length - 1]))
  return { low, high, center: mid }
}
