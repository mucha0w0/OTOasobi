import { useEffect, useRef, useState, type ReactNode } from "react"
import { contains, paneTitles, type LayoutNode } from "./layout"

const viewOrder = Object.keys(paneTitles)

type Props = {
  layout: LayoutNode | null
  canEdit: boolean
  canWrite: boolean
  canAnalyze: boolean
  onOpenFolder: () => void
  onSaveSettings: () => void
  onLoadSettings: () => void
  onWrite: () => void
  onReanalyze: () => void
  onSettings: () => void
  onClose: () => void
  onToggle: (id: string) => void
  onReset: () => void
}

export function MenuBar({
  layout,
  canEdit,
  canWrite,
  canAnalyze,
  onOpenFolder,
  onSaveSettings,
  onLoadSettings,
  onWrite,
  onReanalyze,
  onSettings,
  onClose,
  onToggle,
  onReset,
}: Props) {
  const [open, setOpen] = useState<string | null>(null)
  const barRef = useRef<HTMLElement>(null)

  useEffect(() => {
    const close = (event: PointerEvent) => {
      if (!barRef.current?.contains(event.target as Node))
        setOpen(null)
    }
    window.addEventListener("pointerdown", close)
    return () => window.removeEventListener("pointerdown", close)
  }, [])

  return (
    <nav ref={barRef} className="flex h-9 shrink-0 items-center gap-1 border-b border-[var(--line)] bg-[var(--panel)] px-2 text-sm">
      <Menu title="ファイル" open={open} setOpen={setOpen}>
        <Item onClick={onOpenFolder}>フォルダを開く</Item>
        <Item disabled={!canEdit} onClick={onSaveSettings}>設定を保存</Item>
        <Item disabled={!canEdit} onClick={onLoadSettings}>設定を読み込む</Item>
        <Item disabled={!canWrite} onClick={onWrite}>oto.iniを生成</Item>
        <Item onClick={onClose}>終了</Item>
      </Menu>
      <Menu title="編集" open={open} setOpen={setOpen}>
        <Item onClick={onSettings}>環境設定</Item>
      </Menu>
      <Menu title="解析" open={open} setOpen={setOpen}>
        <Item disabled={!canAnalyze} onClick={onReanalyze}>再解析</Item>
        <Item disabled>選択した音声だけを再解析</Item>
      </Menu>
      <Menu title="表示" open={open} setOpen={setOpen}>
        {viewOrder.map((id) => (
          <Item key={id} checked={contains(layout, id)} onClick={() => onToggle(id)}>
            {paneTitles[id]}
          </Item>
        ))}
        <Item onClick={onReset}>配置を初期状態に戻す</Item>
      </Menu>
    </nav>
  )
}

function Menu({
  title,
  open,
  setOpen,
  children,
}: {
  title: string
  open: string | null
  setOpen: (title: string | null) => void
  children: ReactNode
}) {
  const shown = open === title
  return (
    <div className="relative">
      <button
        type="button"
        className={`rounded px-2 py-1 ${shown ? "bg-[var(--line)]" : ""}`}
        onClick={() => setOpen(shown ? null : title)}
      >
        {title}
      </button>
      {shown && (
        <div
          className="absolute left-0 top-full z-20 mt-1 min-w-64 rounded border border-[var(--line)] bg-[var(--panel)] py-1 shadow"
          onClick={() => setOpen(null)}
        >
          {children}
        </div>
      )}
    </div>
  )
}

function Item({
  children,
  onClick,
  disabled,
  checked,
}: {
  children: ReactNode
  onClick?: () => void
  disabled?: boolean
  checked?: boolean
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      className="block w-full px-3 py-1 text-left text-sm enabled:hover:bg-[var(--line)] disabled:text-[var(--muted)]"
      onClick={onClick}
    >
      {checked ? "✓ " : ""}{children}
    </button>
  )
}
