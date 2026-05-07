// Mendix's WebView host injects different bridge globals per platform:
//
//   Windows (WebView2 host on EdgeHTML/Chromium):
//     window.chrome.webview.postMessage(envelope)                     // JS → C#
//     window.chrome.webview.addEventListener("message", cb)           // C# → JS
//                                                                     // (cb gets MessageEvent with .data = envelope object)
//
//   Mac (WKWebView host, decompiled from Mendix.Modeler.Controls.WebBrowser.MacOS):
//     window.webkit.messageHandlers.studioPro.postMessage(JSON.string) // JS → C#
//                                                                     // (must be a JSON-serialized string, not an object)
//     window.WKPostMessage = (envelope) => { ... }                     // C# → JS
//                                                                     // (Mendix evaluates `window.WKPostMessage && window.WKPostMessage(json)`)
//
// Same C# call site (IWebView.PostMessage / IWebView.MessageReceived) — entirely
// different JS contract. We detect the platform at constructor time and wire
// up the matching pair.

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (msg: any) => void;
        addEventListener: (type: "message", listener: (e: MessageEvent) => void) => void;
        removeEventListener: (type: "message", listener: (e: MessageEvent) => void) => void;
      };
    };
    webkit?: {
      messageHandlers?: {
        [name: string]: { postMessage: (msg: any) => void };
      };
    };
    WKPostMessage?: (envelope: { message: string; data?: any; channelID?: string }) => void;
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

interface BridgeTransport {
  postMessage(env: { message: string; data?: any }): void;
  dispose(): void;
}

export class Bridge {
  private handlers = new Map<string, Set<Handler>>();
  private transport: BridgeTransport;

  constructor() {
    this.transport = createTransport((env) => this.dispatchEnvelope(env));
    // Mendix's WebView host queues C# → JS messages until it sees this magic
    // string. Without it, IWebView.PostMessage calls from C# are buffered
    // and never delivered. Send it as soon as the listener is wired.
    this.send("MessageListenerRegistered");
  }

  dispose() {
    this.transport.dispose();
    this.handlers.clear();
  }

  send(message: string, data?: object): void {
    const env: any = { message };
    if (data !== undefined) env.data = data;
    this.transport.postMessage(env);
  }

  on<T = any>(message: string, handler: Handler<T>): void {
    if (!this.handlers.has(message)) this.handlers.set(message, new Set());
    this.handlers.get(message)!.add(handler as Handler);
  }

  off<T = any>(message: string, handler: Handler<T>): void {
    this.handlers.get(message)?.delete(handler as Handler);
  }

  private dispatchEnvelope(env: any): void {
    if (!env || typeof env.message !== "string") return;
    const set = this.handlers.get(env.message);
    if (!set) return;
    for (const h of set) {
      try { h(env.data); } catch (err) { console.error("bridge handler", err); }
    }
  }
}

function createTransport(onMessage: (env: any) => void): BridgeTransport {
  // WebView2 (Windows): window.chrome.webview is fully formed.
  if (typeof window.chrome?.webview?.postMessage === "function") {
    const cw = window.chrome.webview;
    const bound = (e: MessageEvent) => onMessage(e.data);
    cw.addEventListener("message", bound);
    return {
      postMessage(env) { cw.postMessage(env); },
      dispose() { cw.removeEventListener("message", bound); },
    };
  }

  // WKWebView (Mac): post via window.webkit.messageHandlers.studioPro,
  // receive via a window.WKPostMessage function we define. Mendix's host
  // calls our function from native land via WKWebView.evaluateJavaScript.
  const macHandler = window.webkit?.messageHandlers?.studioPro;
  if (macHandler && typeof macHandler.postMessage === "function") {
    // Mendix's Mac host serializes the envelope to a JSON string and passes
    // that as the single argument: WebJsonSerializer.Serialize(...) → NSString
    // → JS string. So we always parse here. Defensively also accept a real
    // object in case a future Studio Pro version changes the marshaling.
    window.WKPostMessage = ((envOrJson: any) => {
      const env =
        typeof envOrJson === "string" ? JSON.parse(envOrJson) : envOrJson;
      onMessage(env);
    }) as any;
    return {
      postMessage(env) {
        // WKScriptMessageHandler delivers wkMessage.Body which Mendix expects
        // to be an NSString it can JSON-parse, so we serialize on this side.
        macHandler.postMessage(JSON.stringify(env));
      },
      dispose() {
        if (window.WKPostMessage) delete window.WKPostMessage;
      },
    };
  }

  // No bridge available — render an in-page diagnostic instead of throwing
  // synchronously inside the constructor (which would leave the user with a
  // truly blank pane and no clue why).
  throw new Error(
    "No Mendix WebView bridge found. Expected window.chrome.webview (Windows) " +
      "or window.webkit.messageHandlers.studioPro (Mac).",
  );
}
