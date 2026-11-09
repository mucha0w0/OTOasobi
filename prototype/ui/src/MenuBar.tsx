import { useEffect, useRef, useState, type ReactNode } from "react"
import { contains, paneTitles, viewFolders, type LayoutNode } from "./layout"

type Props = {
  layout: LayoutNode | null
  canEdit: boolean
  canWrite: boolean
  canAnalyze: boolean
  canAnalyzeOne: boolean
  onOpenFolder: () => void
  onSaveSettings: () => void
  onLoadSettings: () => void
  onWrite: () => void
  onReanalyze: () => void
  onReanalyzeOne: () => void
  canUndo: boolean
  canRedo: boolean
  onUndo: () => void
  onRedo: () => void
  onSettings: () => void
  onClose: () => void
  onToggle: (id: string) => void
  onReset: () => void
  onRecording: () => void
  templates: string[]
  onTemplate: (name: string) => void
  onSaveTemplate: () => void
  onCheckUpdate: () => void
}

export function MenuBar({
  layout,
  canEdit,
  canWrite,
  canAnalyze,
  canAnalyzeOne,
  onOpenFolder,
  onSaveSettings,
  onLoadSettings,
  onWrite,
  onReanalyze,
  onReanalyzeOne,
  canUndo,
  canRedo,
  onUndo,
  onRedo,
  onSettings,
  onClose,
  onToggle,
  onReset,
  onRecording,
  templates,
  onTemplate,
  onSaveTemplate,
  onCheckUpdate,
}: Props) {
  const [open, setOpen] = useState<string | null>(null)
  const [folder, setFolder] = useState<string | null>(null)
  const barRef = useRef<HTMLElement>(null)

  useEffect(() => {
    const close = (event: PointerEvent) => {
      if (!barRef.current?.contains(event.target as Node))
        setOpen(null)
    }
    window.addEventListener("pointerdown", close)
    return () => window.removeEventListener("pointerdown", close)
  }, [])

  useEffect(() => {
    if (open !== "表示")
      setFolder(null)
  }, [open])

  return (
    <nav ref={barRef} className="relative z-30 flex h-8 shrink-0 items-center gap-1 border-b border-[var(--line)] bg-[var(--panel)] px-2 text-sm">
      <Menu title="ファイル" open={open} setOpen={setOpen}>
        <Item onClick={onOpenFolder}>フォルダを開く</Item>
        <Item disabled={!canEdit} onClick={onSaveSettings}>設定を保存</Item>
        <Item disabled={!canEdit} onClick={onLoadSettings}>設定を読み込む</Item>
        <Item disabled={!canWrite} onClick={onWrite}>oto.iniを生成</Item>
        <Item onClick={onCheckUpdate}>更新を確認</Item>
        <Item onClick={onClose}>終了</Item>
      </Menu>
      <Menu title="編集" open={open} setOpen={setOpen}>
        <Item disabled={!canUndo} onClick={onUndo}>元に戻す</Item>
        <Item disabled={!canRedo} onClick={onRedo}>やり直す</Item>
        <div className="my-1 border-t border-[var(--line)]" />
        <Item onClick={onSettings}>環境設定</Item>
      </Menu>
      <Menu title="解析" open={open} setOpen={setOpen}>
        <Item disabled={!canAnalyze} onClick={onReanalyze}>再解析</Item>
        <Item disabled={!canAnalyzeOne} onClick={onReanalyzeOne}>選択した音声だけを再解析</Item>
      </Menu>
      <Menu title="表示" open={open} setOpen={setOpen}>
        {viewFolders.map((group) => (
          <Flyout
            key={group.title}
            title={group.title}
            open={folder === group.title}
            onOpen={() => setFolder(group.title)}
          >
            {group.ids.map((id) => (
              <Item key={id} checked={contains(layout, id)} onClick={() => onToggle(id)}>
                {paneTitles[id]}
              </Item>
            ))}
          </Flyout>
        ))}
        <div onMouseEnter={() => setFolder(null)}>
          <div className="my-1 border-t border-[var(--line)]" />
          <Item onClick={onReset}>初期配置</Item>
          <Item onClick={onRecording}>録音</Item>
          {templates.map((name) => (
            <Item key={name} onClick={() => onTemplate(name)}>{name}</Item>
          ))}
          <Item onClick={onSaveTemplate}>この配置をテンプレートとして保存</Item>
        </div>
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

function Flyout({
  title,
  open,
  onOpen,
  children,
}: {
  title: string
  open: boolean
  onOpen: () => void
  children: ReactNode
}) {
  const rowRef = useRef<HTMLDivElement>(null)
  const [side, setSide] = useState<"left" | "right">("right")

  useEffect(() => {
    if (!open || !rowRef.current)
      return
    const rect = rowRef.current.getBoundingClientRect()
    setSide(rect.right + 240 > window.innerWidth ? "left" : "right")
  }, [open])

  return (
    <div ref={rowRef} className="relative" onMouseEnter={onOpen}>
      <button
        type="button"
        className={`flex w-full items-center px-3 py-1 text-left text-sm hover:bg-[var(--line)] ${open ? "bg-[var(--line)]" : ""}`}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={(event) => {
          event.stopPropagation()
          onOpen()
        }}
      >
        <span className="min-w-0 flex-1">{title}</span>
        <Chevron />
      </button>
      {open && (
        <div className={`absolute top-0 z-30 min-w-56 rounded border border-[var(--line)] bg-[var(--panel)] py-1 shadow ${side === "left" ? "right-full -mr-px" : "left-full -ml-px"}`}>
          {children}
        </div>
      )}
    </div>
  )
}

function Chevron() {
  return (
    <svg className="shrink-0" width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
      <path d="M6 3.5 L10.5 8 L6 12.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
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
      className="flex w-full items-center px-3 py-1 text-left text-sm enabled:hover:bg-[var(--line)] disabled:text-[var(--muted)]"
      onClick={onClick}
    >
      {checked !== undefined && <span className="inline-block w-4 shrink-0">{checked ? "✓" : ""}</span>}
      {children}
    </button>
  )
}
