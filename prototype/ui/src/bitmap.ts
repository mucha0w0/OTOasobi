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
