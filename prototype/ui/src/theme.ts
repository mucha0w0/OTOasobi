export type Rgb = { r: number; g: number; b: number }

export type ThemeChoice = "light" | "dark" | "system"

export function parseHex(hex: string): Rgb | null {
  const text = hex.trim().replace(/^#/, "")
  if (!/^[0-9a-fA-F]{6}$/.test(text))
    return null
  return {
    r: Number.parseInt(text.slice(0, 2), 16),
    g: Number.parseInt(text.slice(2, 4), 16),
    b: Number.parseInt(text.slice(4, 6), 16),
  }
}

export function relativeLuminance({ r, g, b }: Rgb): number {
  const channel = (value: number) => {
    const s = value / 255
    return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)
}

export function contrastRatio(fill: Rgb, ink: Rgb): number {
  const lighter = Math.max(relativeLuminance(fill), relativeLuminance(ink))
  const darker = Math.min(relativeLuminance(fill), relativeLuminance(ink))
  return (lighter + 0.05) / (darker + 0.05)
}

const black: Rgb = { r: 0, g: 0, b: 0 }
const white: Rgb = { r: 255, g: 255, b: 255 }

export function buttonColors(hex: string): { fill: string; ink: string } {
  const fill = parseHex(hex) ?? { r: 15, g: 118, b: 110 }
  const onBlack = contrastRatio(fill, black)
  const onWhite = contrastRatio(fill, white)
  const ink = onBlack >= onWhite ? "#000000" : "#ffffff"
  return { fill: toHex(fill), ink }
}

export function resolveTheme(choice: ThemeChoice, systemDark: boolean): "light" | "dark" {
  if (choice === "system")
    return systemDark ? "dark" : "light"
  return choice
}

function toHex({ r, g, b }: Rgb): string {
  return `#${[r, g, b].map((value) => value.toString(16).padStart(2, "0")).join("")}`
}
