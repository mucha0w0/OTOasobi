import type { ReactNode } from "react"

export function IconButton({
  label,
  accent,
  disabled,
  onClick,
  children,
}: {
  label: string
  accent?: boolean
  disabled?: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      className={`icon-button ${accent ? "accent-button" : "ghost-button"}`}
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={onClick}
    >
      {children}
    </button>
  )
}

function Glyph({ children }: { children: ReactNode }) {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
      {children}
    </svg>
  )
}

const stroke = {
  stroke: "currentColor",
  strokeWidth: 1.5,
  strokeLinecap: "round" as const,
  strokeLinejoin: "round" as const,
}

export function IconPrev() {
  return (
    <Glyph>
      <path d="M10.5 3.5 L5.5 8 L10.5 12.5" {...stroke} />
    </Glyph>
  )
}

export function IconNext() {
  return (
    <Glyph>
      <path d="M5.5 3.5 L10.5 8 L5.5 12.5" {...stroke} />
    </Glyph>
  )
}

export function IconSkip() {
  return (
    <Glyph>
      <path d="M3 4 L8.5 8 L3 12" {...stroke} />
      <path d="M11.5 4 V12" {...stroke} />
    </Glyph>
  )
}

export function IconUp() {
  return (
    <Glyph>
      <path d="M3.5 10 L8 5 L12.5 10" {...stroke} />
    </Glyph>
  )
}

export function IconDown() {
  return (
    <Glyph>
      <path d="M3.5 6 L8 11 L12.5 6" {...stroke} />
    </Glyph>
  )
}

export function IconRecord() {
  return (
    <Glyph>
      <circle cx="8" cy="8" r="4" fill="currentColor" />
    </Glyph>
  )
}

export function IconStop() {
  return (
    <Glyph>
      <rect x="4" y="4" width="8" height="8" rx="1" fill="currentColor" />
    </Glyph>
  )
}

export function IconPlay() {
  return (
    <Glyph>
      <path d="M5 3.5 L12.5 8 L5 12.5 Z" fill="currentColor" />
    </Glyph>
  )
}

export function IconPen() {
  return (
    <Glyph>
      <path d="M11.2 2.2 L13.8 4.8 L6.2 12.4 L3.2 13.2 L4 10.2 Z" {...stroke} />
    </Glyph>
  )
}
