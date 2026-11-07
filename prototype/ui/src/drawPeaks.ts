export function drawPeaks(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  lo: number[],
  hi: number[],
  color: string,
  panPx = 0,
) {
  const count = Math.min(lo.length, hi.length)
  if (count === 0 || width <= 0 || height <= 0)
    return
  let peak = 0.2
  for (let index = 0; index < count; index++)
    peak = Math.max(peak, Math.abs(lo[index] ?? 0), Math.abs(hi[index] ?? 0))
  const mid = height / 2
  ctx.strokeStyle = color
  ctx.lineWidth = 1
  ctx.beginPath()
  for (let index = 0; index < count; index++) {
    const x = ((index + 0.5) / count) * width + panPx
    const top = mid - ((hi[index] ?? 0) / peak) * (mid - 2)
    const bottom = mid - ((lo[index] ?? 0) / peak) * (mid - 2)
    ctx.moveTo(x, top)
    ctx.lineTo(x, bottom)
  }
  ctx.stroke()
}
