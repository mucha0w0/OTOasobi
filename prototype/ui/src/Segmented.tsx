import { useRef, type KeyboardEvent } from "react"

export function Segmented<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string
  value: T
  options: readonly (readonly [T, string])[]
  onChange: (value: T) => void
}) {
  const list = useRef<HTMLDivElement>(null)

  function move(event: KeyboardEvent<HTMLDivElement>) {
    if (options.length === 0)
      return
    const index = Math.max(0, options.findIndex(([id]) => id === value))
    let next = -1
    if (event.key === "ArrowRight" || event.key === "ArrowDown")
      next = (index + 1) % options.length
    else if (event.key === "ArrowLeft" || event.key === "ArrowUp")
      next = (index - 1 + options.length) % options.length
    else if (event.key === "Home")
      next = 0
    else if (event.key === "End")
      next = options.length - 1
    if (next < 0)
      return
    event.preventDefault()
    const id = options[next][0]
    onChange(id)
    list.current?.querySelector<HTMLButtonElement>(`[data-segment="${CSS.escape(id)}"]`)?.focus()
  }

  return (
    <div ref={list} role="tablist" aria-label={label} className="segment" onKeyDown={move}>
      {options.map(([id, text]) => {
        const selected = id === value
        return (
          <button
            key={id}
            type="button"
            role="tab"
            data-segment={id}
            aria-selected={selected}
            tabIndex={selected ? 0 : -1}
            title={text}
            className={selected ? "segment-item is-on" : "segment-item"}
            onClick={() => onChange(id)}
          >
            {text}
          </button>
        )
      })}
    </div>
  )
}
