const stops: ReadonlyArray<readonly [number, number, number]> = [
  [0, 0, 4],
  [28, 16, 68],
  [79, 18, 123],
  [129, 37, 129],
  [181, 54, 122],
  [229, 80, 100],
  [251, 135, 97],
  [254, 194, 135],
  [252, 253, 191],
]

function rint(value: number) {
  const floor = Math.floor(value)
  const frac = value - floor
  if (frac > 0.5)
    return floor + 1
  if (frac < 0.5)
    return floor
  return floor % 2 === 0 ? floor : floor + 1
}

export function toneLut(): Uint8ClampedArray {
  const lut = new Uint8ClampedArray(256 * 3)
  const last = stops.length - 1
  for (let index = 0; index < 256; index++) {
    const pos = (last * index) / 255
    const i0 = Math.floor(pos)
    const i1 = Math.min(i0 + 1, last)
    const blend = pos - i0
    const from = stops[i0] ?? stops[0]
    const to = stops[i1] ?? from
    lut[index * 3] = rint(from[0] * (1 - blend) + to[0] * blend)
    lut[index * 3 + 1] = rint(from[1] * (1 - blend) + to[1] * blend)
    lut[index * 3 + 2] = rint(from[2] * (1 - blend) + to[2] * blend)
  }
  return lut
}

const lut = toneLut()
const frames = new WeakMap<Uint8Array, ImageData>()

export function decodeBase64(value: string): Uint8Array {
  const binary = atob(value)
  const bytes = new Uint8Array(binary.length)
  for (let index = 0; index < binary.length; index++)
    bytes[index] = binary.charCodeAt(index)
  return bytes
}

export function specImage(indices: Uint8Array, columns: number, rows: number): ImageData | null {
  const cached = frames.get(indices)
  if (cached && cached.width === columns && cached.height === rows)
    return cached
  if (columns < 1 || rows < 1 || indices.length < columns * rows)
    return null
  const image = new ImageData(columns, rows)
  const data = image.data
  const count = columns * rows
  for (let index = 0; index < count; index++) {
    const tone = (indices[index] ?? 0) * 3
    const offset = index * 4
    data[offset] = lut[tone] ?? 0
    data[offset + 1] = lut[tone + 1] ?? 0
    data[offset + 2] = lut[tone + 2] ?? 0
    data[offset + 3] = 255
  }
  frames.set(indices, image)
  return image
}

export function paintIndices(
  ctx: CanvasRenderingContext2D,
  indices: Uint8Array,
  columns: number,
  rows: number,
) {
  const image = specImage(indices, columns, rows)
  if (!image)
    return false
  ctx.putImageData(image, 0, 0)
  return true
}
