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
  const [folders, setFolders] = useState<Record<string, boolean>>({})
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
    <nav ref={barRef} className="relative z-30 flex h-9 shrink-0 items-center gap-1 border-b border-[var(--line)] bg-[var(--panel)] px-2 text-sm">
      <Menu title="ファイル" open={open} setOpen={setOpen}>
        <Item onClick={onOpenFolder}>フォルダを開く</Item>
        <Item disabled={!canEdit} onClick={onSaveSettings}>設定を保存</Item>
        <Item disabled={!canEdit} onClick={onLoadSettings}>設定を読み込む</Item>
        <Item disabled={!canWrite} onClick={onWrite}>oto.iniを生成</Item>
        <Item onClick={onCheckUpdate}>更新を確認</Item>
        <Item onClick={onClose}>終了</Item>
      </Menu>
      <Menu title="編集" open={open} setOpen={setOpen}>
        <Item onClick={onSettings}>環境設定</Item>
      </Menu>
      <Menu title="解析" open={open} setOpen={setOpen}>
        <Item disabled={!canAnalyze} onClick={onReanalyze}>再解析</Item>
        <Item disabled={!canAnalyzeOne} onClick={onReanalyzeOne}>選択した音声だけを再解析</Item>
      </Menu>
      <Menu title="表示" open={open} setOpen={setOpen} scroll>
        {viewFolders.map((folder) => {
          const expanded = folders[folder.title] !== false
          return (
            <div key={folder.title}>
              <button
                type="button"
                className="flex w-full items-center gap-1 px-3 py-1 text-left text-sm hover:bg-[var(--line)]"
                aria-expanded={expanded}
                onClick={(event) => {
                  event.stopPropagation()
                  setFolders((current) => ({ ...current, [folder.title]: !expanded }))
                }}
              >
                <span className="inline-block w-3 shrink-0 text-[var(--muted)]">{expanded ? "▼" : "▶"}</span>
                {folder.title}
              </button>
              {expanded && folder.ids.map((id) => (
                <Item key={id} indent checked={contains(layout, id)} onClick={() => onToggle(id)}>
                  {paneTitles[id]}
                </Item>
              ))}
            </div>
          )
        })}
        <div className="my-1 border-t border-[var(--line)]" />
        <Item onClick={onReset}>初期配置</Item>
        <Item onClick={onRecording}>録音</Item>
        {templates.map((name) => (
          <Item key={name} onClick={() => onTemplate(name)}>{name}</Item>
        ))}
        <Item onClick={onSaveTemplate}>この配置をテンプレートとして保存</Item>
      </Menu>
    </nav>
  )
}

function Menu({
  title,
  open,
  setOpen,
  scroll,
  children,
}: {
  title: string
  open: string | null
  setOpen: (title: string | null) => void
  scroll?: boolean
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
          className={`absolute left-0 top-full z-20 mt-1 min-w-64 rounded border border-[var(--line)] bg-[var(--panel)] py-1 shadow ${scroll ? "max-h-[calc(100vh-4rem)] overflow-y-auto" : ""}`}
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
  indent,
}: {
  children: ReactNode
  onClick?: () => void
  disabled?: boolean
  checked?: boolean
  indent?: boolean
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      className={`flex w-full items-center py-1 text-left text-sm enabled:hover:bg-[var(--line)] disabled:text-[var(--muted)] ${indent ? "pr-3 pl-7" : "px-3"}`}
      onClick={onClick}
    >
      {checked !== undefined && <span className="inline-block w-4 shrink-0">{checked ? "✓" : ""}</span>}
      {children}
    </button>
  )
}
