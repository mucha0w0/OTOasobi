import { createContext, useCallback, useContext, useLayoutEffect, useRef, useState, type ReactNode } from "react"
import { createPortal } from "react-dom"

type Slots = {
  attach: (id: string, element: HTMLDivElement | null) => void
}

const SlotContext = createContext<Slots | null>(null)

export function assignHost<T>(current: Record<string, T>, id: string, element: T | null): Record<string, T> {
  if ((current[id] ?? null) === element)
    return current
  if (element === null) {
    if (!(id in current))
      return current
    const next = { ...current }
    delete next[id]
    return next
  }
  return { ...current, [id]: element }
}

// ドックの枠は配置のたびに作り直る。中身は id に残し、別の枠のキャンバスを流用しない。
export function PaneSlots({ ids, render, children }: { ids: string[]; render: (id: string) => ReactNode; children: ReactNode }) {
  const [targets, setTargets] = useState<Record<string, HTMLDivElement>>({})
  const attach = useCallback((id: string, element: HTMLDivElement | null) => {
    setTargets((current) => assignHost(current, id, element))
  }, [])
  return (
    <SlotContext.Provider value={{ attach }}>
      {children}
      {ids.map((id) => {
        const element = targets[id]
        if (!element)
          return null
        return <PanePortal key={id} element={element} render={render} id={id} />
      })}
    </SlotContext.Provider>
  )
}

function PanePortal({ element, render, id }: { element: HTMLElement; render: (id: string) => ReactNode; id: string }) {
  return createPortal(render(id), element)
}

export function PaneHost({ id }: { id: string }) {
  const slots = useContext(SlotContext)
  const attach = slots?.attach
  const ref = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const element = ref.current
    if (!element || !attach)
      return
    attach(id, element)
    return () => attach(id, null)
  }, [attach, id])
  return <div ref={ref} className="min-h-0 min-w-0 flex-1" />
}
