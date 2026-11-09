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

export function buttonColors(hex: string, theme: "light" | "dark"): { fill: string; ink: string } {
  const parsed = parseHex(hex) ?? parseHex(defaultAccent(theme))!
  if (theme === "dark") {
    const onBlack = contrastRatio(parsed, black)
    const onWhite = contrastRatio(parsed, white)
    return { fill: toHex(parsed), ink: onBlack >= onWhite ? "#000000" : "#ffffff" }
  }
  return { fill: toHex(darkenForWhiteText(parsed)), ink: "#ffffff" }
}

// 白文字が 4.5:1 に足りないときだけ、色相を残して明度を下げる。
function darkenForWhiteText(fill: Rgb): Rgb {
  if (contrastRatio(fill, white) >= 4.5)
    return fill
  const oklch = rgbToOklch(fill)
  let lo = 0
  let hi = oklch.L
  let best = 0
  for (let i = 0; i < 24; i++) {
    const mid = (lo + hi) / 2
    if (contrastRatio(oklchToRgb({ ...oklch, L: mid }), white) >= 4.5) {
      best = mid
      lo = mid
    } else {
      hi = mid
    }
  }
  return oklchToRgb({ ...oklch, L: best })
}

export function defaultAccent(theme: "light" | "dark"): string {
  return theme === "dark" ? "#2bb8a0" : "#0f766e"
}

type Oklch = { L: number; C: number; H: number }

export function correctAccent(hex: string, theme: "light" | "dark"): string {
  const rgb = parseHex(hex)
  if (!rgb)
    return defaultAccent(theme)
  const oklch = rgbToOklch(rgb)
  const [min, max] = theme === "dark" ? [0.65, 0.85] : [0.38, 0.58]
  if (oklch.L >= min && oklch.L <= max)
    return toHex(rgb)
  return toHex(oklchToRgb({ ...oklch, L: Math.min(max, Math.max(min, oklch.L)) }))
}

export function oklchOf(hex: string): Oklch | null {
  const rgb = parseHex(hex)
  return rgb ? rgbToOklch(rgb) : null
}

function rgbToOklch({ r, g, b }: Rgb): Oklch {
  const rl = linearize(r / 255)
  const gl = linearize(g / 255)
  const bl = linearize(b / 255)
  const l = Math.cbrt(0.4122214708 * rl + 0.5363325363 * gl + 0.0514459929 * bl)
  const m = Math.cbrt(0.2119034982 * rl + 0.6806995451 * gl + 0.1073969566 * bl)
  const s = Math.cbrt(0.0883024619 * rl + 0.2817188376 * gl + 0.6299787005 * bl)
  const L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s
  const a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s
  const b2 = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s
  const C = Math.hypot(a, b2)
  let H = Math.atan2(b2, a) * 180 / Math.PI
  if (H < 0)
    H += 360
  return { L, C, H }
}

function oklchToRgb({ L, C, H }: Oklch): Rgb {
  const radians = H * Math.PI / 180
  const a = C * Math.cos(radians)
  const b = C * Math.sin(radians)
  const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
  const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
  const s = (L - 0.0894841775 * a - 1.2914855480 * b) ** 3
  return {
    r: channel(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
    g: channel(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
    b: channel(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s),
  }
}

function linearize(value: number) {
  return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
}

function channel(value: number) {
  const clamped = Math.min(1, Math.max(0, value))
  const encoded = clamped <= 0.0031308 ? 12.92 * clamped : 1.055 * clamped ** (1 / 2.4) - 0.055
  return Math.round(Math.min(1, Math.max(0, encoded)) * 255)
}

export function resolveTheme(choice: ThemeChoice, systemDark: boolean): "light" | "dark" {
  if (choice === "system")
    return systemDark ? "dark" : "light"
  return choice
}

function toHex({ r, g, b }: Rgb): string {
  return `#${[r, g, b].map((value) => value.toString(16).padStart(2, "0")).join("")}`
}
