import { useRef, type ReactNode, type PointerEvent as ReactPointerEvent } from "react"
import { paneTitles, type LayoutNode, type PaneBox } from "./layout"

type Props = {
  layout: LayoutNode | null
  boxes: { current: Record<string, PaneBox> }
  onRatio: (path: number[], ratio: number) => void
  onResizing: (holding: boolean) => void
  render: (id: string) => ReactNode
}

type NodeProps = Omit<Props, "layout"> & {
  node: LayoutNode
  path: number[]
}

export function Dock({ layout, boxes, onRatio, onResizing, render }: Props) {
  if (!layout)
    return <div className="h-full bg-[var(--bg)]" />
  return <Node node={layout} path={[]} boxes={boxes} onRatio={onRatio} onResizing={onResizing} render={render} />
}

function Node({ node, path, boxes, onRatio, onResizing, render }: NodeProps) {
  const frameRef = useRef<HTMLDivElement>(null)
  if ("id" in node) {
    return (
      <section
        className="flex h-full min-h-0 min-w-0 flex-col overflow-hidden bg-[var(--panel)]"
        ref={(element) => {
          if (!element)
            return
          const rect = element.getBoundingClientRect()
          boxes.current[node.id] = { width: rect.width, height: rect.height }
        }}
      >
        <div className="flex h-8 shrink-0 items-center border-b border-[var(--line)] px-2 text-xs text-[var(--muted)]">
          {paneTitles[node.id] ?? node.id}
        </div>
        <div className="min-h-0 flex-1">{render(node.id)}</div>
      </section>
    )
  }

  const horizontal = node.axis === "h"
  return (
    <div ref={frameRef} className={`flex h-full min-h-0 min-w-0 ${horizontal ? "" : "flex-col"}`}>
      <div
        className="min-h-0 min-w-0"
        style={horizontal ? { width: `${node.ratio * 100}%` } : { height: `${node.ratio * 100}%` }}
      >
        <Node key={path.join(".") + ".0"} node={node.first} path={[...path, 0]} boxes={boxes} onRatio={onRatio} onResizing={onResizing} render={render} />
      </div>
      <div
        className={horizontal ? "w-1.5 shrink-0 cursor-col-resize bg-[var(--line)]" : "h-1.5 shrink-0 cursor-row-resize bg-[var(--line)]"}
        onPointerDown={(event) => startDrag(event, frameRef.current, horizontal, node.ratio, path, onRatio, onResizing)}
      />
      <div className="min-h-0 min-w-0 flex-1">
        <Node key={path.join(".") + ".1"} node={node.second} path={[...path, 1]} boxes={boxes} onRatio={onRatio} onResizing={onResizing} render={render} />
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
