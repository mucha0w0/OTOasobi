export function nextFileIndex(index: number, count: number, delta: number): number {
  if (count <= 0)
    return -1
  const base = index < 0 ? 0 : index
  return Math.min(count - 1, Math.max(0, base + delta))
}

const pixelNotch = 100

/** マウスの1ノッチを1段にする。画素の端数と、閾値未満のトラックパッドだけ繰り越す。 */
export function takeWheelSteps(carry: number, deltaY: number, deltaMode: number): { steps: number; carry: number } {
  if (deltaMode === 1) {
    const sum = carry + deltaY
    const steps = Math.trunc(sum)
    return { steps, carry: sum - steps }
  }
  if (deltaMode === 2)
    return { steps: Math.trunc(deltaY) * 3, carry: 0 }
  if (Math.abs(deltaY) >= pixelNotch / 2) {
    const steps = Math.trunc(deltaY / pixelNotch)
    return { steps: steps === 0 ? Math.sign(deltaY) : steps, carry: 0 }
  }
  const sum = carry + deltaY
  if (Math.abs(sum) < pixelNotch)
    return { steps: 0, carry: sum }
  return { steps: Math.sign(sum), carry: 0 }
}
