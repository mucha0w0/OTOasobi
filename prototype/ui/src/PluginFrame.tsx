import { useEffect, useRef } from "react"
import type { PageCommand, PluginItem, PluginReply } from "./protocol"

export type PluginContext = {
  theme: "light" | "dark"
  accent: string
  scale: number
  file: string
  selected: string[]
}

type Props = {
  item: PluginItem
  context: PluginContext
  onCall: (command: PageCommand) => void
  bind: (id: string, send: ((reply: PluginReply) => void) | null) => void
}

export function PluginFrame({ item, context, onCall, bind }: Props) {
  const frame = useRef<HTMLIFrameElement>(null)
  const contextRef = useRef(context)
  const onCallRef = useRef(onCall)
  contextRef.current = context
  onCallRef.current = onCall
  const signature = `${context.theme}\n${context.accent}\n${context.scale}\n${context.file}\n${context.selected.join("\n")}`

  useEffect(() => {
    bind(item.id, (reply) => {
      frame.current?.contentWindow?.postMessage({
        type: "otoasobi",
        request: reply.request,
        ok: reply.ok,
        result: reply.result,
        error: reply.error,
      }, item.origin)
    })
    return () => bind(item.id, null)
  }, [bind, item.id, item.origin])

  useEffect(() => {
    const onMessage = (event: MessageEvent) => {
      if (event.origin !== item.origin || event.source !== frame.current?.contentWindow)
        return
      const data = event.data as { type?: unknown; request?: unknown; op?: unknown; path?: unknown; text?: unknown; encoding?: unknown; body?: unknown } | null
      if (!data || data.type !== "otoasobi" || typeof data.request !== "string")
        return
      if (data.op !== "list" && data.op !== "read" && data.op !== "write" && data.op !== "run")
        return
      onCallRef.current({
        type: "plugin",
        id: item.id,
        request: data.request,
        op: data.op,
        path: typeof data.path === "string" ? data.path : undefined,
        text: typeof data.text === "string" ? data.text : undefined,
        encoding: data.encoding === "cp932" || data.encoding === "utf-8" ? data.encoding : undefined,
        body: data.body,
      })
    }
    window.addEventListener("message", onMessage)
    return () => window.removeEventListener("message", onMessage)
  }, [item.id, item.origin])

  useEffect(() => {
    frame.current?.contentWindow?.postMessage({ type: "otoasobi", context: contextRef.current }, item.origin)
  }, [item.origin, signature])

  return (
    <iframe
      ref={frame}
      title={item.title}
      src={`${item.origin}/${item.entry}`}
      sandbox="allow-scripts allow-same-origin"
      className="h-full w-full border-0 bg-[var(--panel)]"
      onLoad={() => frame.current?.contentWindow?.postMessage({ type: "otoasobi", context: contextRef.current }, item.origin)}
    />
  )
}
