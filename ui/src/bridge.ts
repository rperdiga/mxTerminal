declare global {
  interface Window {
    chrome: {
      webview: {
        postMessage: (msg: any) => void;
        addEventListener: (type: "message", listener: (e: MessageEvent) => void) => void;
        removeEventListener: (type: "message", listener: (e: MessageEvent) => void) => void;
      };
    };
  }
}

export function encodeBase64(bytes: Uint8Array): string {
  if (bytes.length === 0) return "";
  let s = "";
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]!);
  return btoa(s);
}

export function decodeBase64(b64: string): Uint8Array {
  if (b64.length === 0) return new Uint8Array();
  const s = atob(b64);
  const bytes = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) bytes[i] = s.charCodeAt(i);
  return bytes;
}

type Handler<T = any> = (data: T) => void;

export class Bridge {
  private handlers = new Map<string, Set<Handler>>();
  private bound = (e: MessageEvent) => this.dispatch(e);

  constructor() {
    (window as any).chrome.webview.addEventListener("message", this.bound);
  }

  dispose() {
    (window as any).chrome.webview.removeEventListener("message", this.bound);
    this.handlers.clear();
  }

  send(message: string, data?: object): void {
    const env: any = { message };
    if (data !== undefined) env.data = data;
    (window as any).chrome.webview.postMessage(env);
  }

  on<T = any>(message: string, handler: Handler<T>): void {
    if (!this.handlers.has(message)) this.handlers.set(message, new Set());
    this.handlers.get(message)!.add(handler as Handler);
  }

  off<T = any>(message: string, handler: Handler<T>): void {
    this.handlers.get(message)?.delete(handler as Handler);
  }

  private dispatch(e: MessageEvent): void {
    const env = e.data;
    if (!env || typeof env.message !== "string") return;
    const set = this.handlers.get(env.message);
    if (!set) return;
    for (const h of set) {
      try { h(env.data); } catch (err) { console.error("bridge handler", err); }
    }
  }
}
