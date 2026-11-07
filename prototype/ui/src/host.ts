import type { HostEvent, PageCommand } from "./protocol"
import { parseHostEvent } from "./protocol"

type WebView = {
  postMessage(message: unknown): void
  addEventListener(type: "message", listener: (event: { data: unknown }) => void): void
  removeEventListener(type: "message", listener: (event: { data: unknown }) => void): void
}

declare global {
  interface Window {
    chrome?: { webview?: WebView }
  }
}

export function hostAvailable(): boolean {
  return window.chrome?.webview != null
}

export function connectHost(onEvent: (event: HostEvent) => void): {
  send: (command: PageCommand) => void
  close: () => void
} {
  const webview = window.chrome?.webview
  if (!webview)
    return { send: () => undefined, close: () => undefined }

  const listener = (event: { data: unknown }) => {
    const parsed = parseHostEvent(event.data)
    if (parsed)
      onEvent(parsed)
  }
  webview.addEventListener("message", listener)
  return {
    send: (command) => webview.postMessage(command),
    close: () => webview.removeEventListener("message", listener),
  }
}
