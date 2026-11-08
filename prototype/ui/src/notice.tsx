export type NoticeTone = "info" | "success" | "error"

export function noticeTone(message: string): NoticeTone {
  if (message.includes("ありません") || message.includes("失敗") || message.includes("できません"))
    return "error"
  if (message.includes("ました"))
    return "success"
  return "info"
}

export function ToneIcon({ tone }: { tone: NoticeTone }) {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" className="shrink-0">
      {tone === "success" && <path d="M3.5 8.5 L6.5 11.5 L12.5 4.5" fill="none" stroke="var(--success)" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />}
      {tone === "error" && <path d="M8 3.2 V9.2 M8 11.6 V12.2" fill="none" stroke="var(--danger)" strokeWidth="1.5" strokeLinecap="round" />}
      {tone === "info" && (
        <>
          <circle cx="8" cy="8" r="5.25" fill="none" stroke="var(--muted)" strokeWidth="1.5" />
          <path d="M8 7.2 V11.2 M8 5 V5.6" fill="none" stroke="var(--muted)" strokeWidth="1.5" strokeLinecap="round" />
        </>
      )}
    </svg>
  )
}

export function ToneRow({ tone, children, className = "mt-2" }: { tone: NoticeTone; children: string; className?: string }) {
  const color = tone === "success" ? "var(--success)" : tone === "error" ? "var(--danger)" : "var(--muted)"
  return (
    <p role={tone === "error" ? "alert" : "status"} className={`${className} flex items-start gap-2 border-l-[3px] py-0.5 pl-2 text-sm`} style={{ borderColor: color }}>
      <ToneIcon tone={tone} />
      <span>{children}</span>
    </p>
  )
}
