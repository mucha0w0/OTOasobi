// GPU のキャンバスは、要素を動かしたり拡大したりすると別のキャンバスの絵を出す。
// 読み戻し指定で、その面のバッファをソフトウェア描画に固定する。
export function pageZoom() {
  const inline = Number.parseFloat(document.documentElement.style.zoom)
  if (Number.isFinite(inline) && inline > 0)
    return inline
  const computed = Number.parseFloat(getComputedStyle(document.documentElement).zoom)
  return Number.isFinite(computed) && computed > 0 ? computed : 1
}

export function bitmapRatio() {
  return (window.devicePixelRatio || 1) * pageZoom()
}

export function canvasContext(canvas: HTMLCanvasElement) {
  return canvas.getContext("2d", { willReadFrequently: true })
}

export function shouldReplaceBitmap(
  holding: boolean,
  currentWidth: number,
  currentHeight: number,
  nextWidth: number,
  nextHeight: number,
): boolean {
  if (holding)
    return false
  return currentWidth !== nextWidth || currentHeight !== nextHeight
}
