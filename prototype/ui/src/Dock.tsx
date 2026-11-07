import { useRef, useState, type PointerEvent as ReactPointerEvent, type ReactNode } from "react"
import { armBox, hitDrop, type DropAim, type HitLeaf, type HitRegion, type ScreenBox } from "./dockHit"
import { commitMove, hidePane, idsOf, paneTitles, type LayoutNode, type PaneBox } from "./layout"

type Props = {
  layout: LayoutNode | null
  boxes: { current: Record<string, PaneBox> }
  onChange: (next: LayoutNode | null, persist: boolean) => void
  onRatio: (path: number[], ratio: number) => void
  onResizing: (holding: boolean) => void
  render: (id: string) => ReactNode
}

type NodeProps = Omit<Props, "layout" | "onChange"> & {
  node: LayoutNode
  path: number[]
  onTitleDown: (event: ReactPointerEvent<HTMLDivElement>, id: string) => void
  onClose: (id: string) => void
}

type Ghost = { x: number; y: number; title: string }

export function Dock({ layout, boxes, onChange, onRatio, onResizing, render }: Props) {
  const rootRef = useRef<HTMLDivElement>(null)
  const dragRef = useRef<{ id: string; title: string; origin: LayoutNode; startX: number; startY: number; moving: boolean } | null>(null)
  const [ghost, setGhost] = useState<Ghost | null>(null)
  const [aim, setAim] = useState<DropAim | null>(null)

  function onTitleDown(event: ReactPointerEvent<HTMLDivElement>, id: string) {
    if (event.button !== 0 || !layout)
      return
    const session = {
      id,
      title: paneTitles[id] ?? id,
      origin: layout,
      startX: event.clientX,
      startY: event.clientY,
      moving: false,
    }
    dragRef.current = session
    const move = (ev: PointerEvent) => {
      const current = dragRef.current
      if (!current)
        return
      const dx = ev.clientX - current.startX
      const dy = ev.clientY - current.startY
      if (!current.moving) {
        if (dx * dx + dy * dy < 16)
          return
        current.moving = true
        onResizing(true)
        onChange(hidePane(current.origin, current.id), false)
      }
      setGhost({ x: ev.clientX, y: ev.clientY, title: current.title })
      const root = rootRef.current
      setAim(root ? hitDrop(...readHits(root), current.id, ev.clientX, ev.clientY) : null)
    }
    const up = (ev: PointerEvent) => {
      window.removeEventListener("pointermove", move)
      window.removeEventListener("pointerup", up)
      const current = dragRef.current
      dragRef.current = null
      setGhost(null)
      setAim(null)
      if (!current?.moving)
        return
      const root = rootRef.current
      const hit = root ? hitDrop(...readHits(root), current.id, ev.clientX, ev.clientY) : null
      const next = commitMove(
        current.origin,
        current.id,
        hit?.edge && hit.targetIds.length ? hit.targetIds : null,
        hit?.edge ?? null,
      )
      onChange(next, true)
      window.requestAnimationFrame(() => window.requestAnimationFrame(() => onResizing(false)))
    }
    window.addEventListener("pointermove", move)
    window.addEventListener("pointerup", up)
  }

  const origin = rootRef.current?.getBoundingClientRect()
  return (
    <div ref={rootRef} className="relative h-full min-h-0 bg-[var(--bg)]">
      {layout && (
        <Node
          node={layout}
          path={[]}
          boxes={boxes}
          onRatio={onRatio}
          onResizing={onResizing}
          render={render}
          onTitleDown={onTitleDown}
          onClose={(id) => onChange(hidePane(layout, id), true)}
        />
      )}
      {aim?.highlight && origin && <Shade box={aim.highlight} origin={origin} />}
      {aim?.arms && origin && <Arms rect={aim.arms} hot={aim.hot ? aim.edge : null} origin={origin} />}
      {ghost && (
        <div
          className="pointer-events-none fixed z-50 rounded border border-[var(--line)] bg-[var(--panel)] px-2 py-1 text-xs text-[var(--text)] shadow"
          style={{ left: ghost.x + 14, top: ghost.y + 14 }}
        >
          {ghost.title}
        </div>
      )}
    </div>
  )
}

function Shade({ box, origin }: { box: ScreenBox; origin: DOMRect }) {
  return (
    <div
      className="pointer-events-none absolute z-20 border border-[var(--accent)] bg-[var(--accent)]/25"
      style={placed(box, origin)}
    />
  )
}

function Arms({ rect, hot, origin }: { rect: ScreenBox; hot: DropAim["edge"]; origin: DOMRect }) {
  return (
    <>
      {(["left", "right", "top", "bottom"] as const).map((edge) => {
        const box = armBox(rect, edge)
        return (
          <div
            key={edge}
            className={`pointer-events-none absolute z-30 grid place-items-center rounded border text-[10px] ${hot === edge ? "border-[var(--accent)] bg-[var(--panel)] text-[var(--text)]" : "border-[var(--line)] bg-[var(--panel)] text-[var(--muted)]"}`}
            style={placed(box, origin)}
          >
            {edge === "left" ? "左" : edge === "right" ? "右" : edge === "top" ? "上" : "下"}
          </div>
        )
      })}
    </>
  )
}

function placed(box: ScreenBox, origin: DOMRect) {
  return {
    left: box.x - origin.left,
    top: box.y - origin.top,
    width: box.width,
    height: box.height,
  }
}

function readHits(root: HTMLElement): [HitLeaf[], HitRegion[]] {
  const leaves: HitLeaf[] = []
  const regions: HitRegion[] = []
  for (const element of root.querySelectorAll<HTMLElement>("[data-dock-node]")) {
    const rect = element.getBoundingClientRect()
    const box = { x: rect.left, y: rect.top, width: rect.width, height: rect.height }
    const ids = (element.dataset.ids ?? "").split(",").filter(Boolean)
    regions.push({ ids, rect: box })
    if (element.dataset.pane)
      leaves.push({ id: element.dataset.pane, rect: box })
  }
  return [leaves, regions]
}

function Node({ node, path, boxes, onRatio, onResizing, render, onTitleDown, onClose }: NodeProps) {
  const frameRef = useRef<HTMLDivElement>(null)
  const ids = idsOf(node).join(",")
  if ("id" in node) {
    const title = paneTitles[node.id] ?? node.id
    return (
      <section
        data-dock-node=""
        data-ids={ids}
        data-pane={node.id}
        className="flex h-full min-h-0 min-w-0 flex-col overflow-hidden bg-[var(--panel)]"
        ref={(element) => {
          if (!element)
            return
          const rect = element.getBoundingClientRect()
          boxes.current[node.id] = { width: rect.width, height: rect.height }
        }}
      >
        <div
          className="flex h-8 shrink-0 cursor-grab items-center border-b border-[var(--line)] select-none"
          onPointerDown={(event) => onTitleDown(event, node.id)}
        >
          <span className="min-w-0 flex-1 truncate px-2 text-xs text-[var(--muted)]">{title}</span>
          <button
            type="button"
            className="mr-1 grid h-5 w-5 place-items-center rounded text-sm leading-none text-[var(--muted)] hover:bg-[var(--line)]"
            aria-label={`${title}を閉じる`}
            title="閉じます"
            onPointerDown={(event) => event.stopPropagation()}
            onClick={() => onClose(node.id)}
          >
            ×
          </button>
        </div>
        <div className="min-h-0 flex-1">{render(node.id)}</div>
      </section>
    )
  }

  const horizontal = node.axis === "h"
  return (
    <div
      ref={frameRef}
      data-dock-node=""
      data-ids={ids}
      className={`flex h-full min-h-0 min-w-0 ${horizontal ? "" : "flex-col"}`}
    >
      <div
        className="min-h-0 min-w-0"
        style={horizontal ? { width: `${node.ratio * 100}%` } : { height: `${node.ratio * 100}%` }}
      >
        <Node key={path.join(".") + ".0"} node={node.first} path={[...path, 0]} boxes={boxes} onRatio={onRatio} onResizing={onResizing} render={render} onTitleDown={onTitleDown} onClose={onClose} />
      </div>
      <div
        className={horizontal ? "w-1.5 shrink-0 cursor-col-resize bg-[var(--line)]" : "h-1.5 shrink-0 cursor-row-resize bg-[var(--line)]"}
        onPointerDown={(event) => startDrag(event, frameRef.current, horizontal, node.ratio, path, onRatio, onResizing)}
      />
      <div className="min-h-0 min-w-0 flex-1">
        <Node key={path.join(".") + ".1"} node={node.second} path={[...path, 1]} boxes={boxes} onRatio={onRatio} onResizing={onResizing} render={render} onTitleDown={onTitleDown} onClose={onClose} />
      </div>
    </div>
  )
}

function startDrag(
  event: ReactPointerEvent<HTMLDivElement>,
  frame: HTMLDivElement | null,
  horizontal: boolean,
  ratio: number,
  path: number[],
  onRatio: (path: number[], ratio: number) => void,
  onResizing: (holding: boolean) => void,
) {
  if (!frame)
    return
  const bounds = frame.getBoundingClientRect()
  const first = event.currentTarget.previousElementSibling as HTMLElement | null
  onResizing(true)
  const move = (ev: PointerEvent) => {
    const total = horizontal ? bounds.width : bounds.height
    const origin = horizontal ? bounds.left : bounds.top
    const pointer = horizontal ? ev.clientX : ev.clientY
    const min = Math.min(0.5, 160 / Math.max(total, 1))
    const next = Math.min(1 - min, Math.max(min, (pointer - origin) / Math.max(total, 1)))
    if (first)
      first.style[horizontal ? "width" : "height"] = `${next * 100}%`
    ratio = next
  }
  const up = () => {
    window.removeEventListener("pointermove", move)
    window.removeEventListener("pointerup", up)
    onResizing(false)
    onRatio(path, ratio)
  }
  window.addEventListener("pointermove", move)
  window.addEventListener("pointerup", up)
  event.currentTarget.setPointerCapture(event.pointerId)
}
