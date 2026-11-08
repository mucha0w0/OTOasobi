export function nextFileIndex(index: number, count: number, delta: number): number {
  if (count <= 0)
    return -1
  const base = index < 0 ? 0 : index
  return Math.min(count - 1, Math.max(0, base + delta))
}
