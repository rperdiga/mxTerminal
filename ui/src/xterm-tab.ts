import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import { WebLinksAddon } from "@xterm/addon-web-links";
import xtermCss from "@xterm/xterm/css/xterm.css";
import { ThemeName, XtermThemes } from "./theme.js";

let cssInjected = false;
function ensureCssInjected() {
  if (cssInjected) return;
  const style = document.createElement("style");
  style.textContent = xtermCss;
  document.head.appendChild(style);
  cssInjected = true;
}

export interface XtermTabOptions {
  scrollbackLines: number;
  theme: ThemeName;
  onInput: (bytes: Uint8Array) => void;
  onResize: (cols: number, rows: number) => void;
}

export class XtermTab {
  readonly host: HTMLDivElement;
  private term: Terminal;
  private fit: FitAddon;
  private docPasteHandler?: (ev: ClipboardEvent) => void;

  constructor(opts: XtermTabOptions) {
    ensureCssInjected();
    this.host = document.createElement("div");
    this.host.className = "terminal-host";

    this.term = new Terminal({
      scrollback: opts.scrollbackLines,
      fontFamily: "Cascadia Mono, Consolas, 'Courier New', monospace",
      fontSize: 13,
      theme: XtermThemes[opts.theme],
      allowProposedApi: true,
      cursorBlink: true,
    });
    this.fit = new FitAddon();
    this.term.loadAddon(this.fit);
    this.term.loadAddon(new WebLinksAddon());
    this.term.open(this.host);

    // Paste interceptor — attached at document level with capture: true so
    // it runs BEFORE any other paste listener anywhere in the DOM (including
    // xterm's own listener on the screen element, and any default browser
    // text-insertion-into-textarea behaviour that fires an `input` event
    // xterm interprets as a second data submission).
    //
    // Only acts when the focused element is inside our host so we don't
    // hijack pastes meant for other extensions sharing the WebView.
    this.docPasteHandler = (ev: ClipboardEvent) => {
      const focused = document.activeElement;
      if (!focused || !this.host.contains(focused)) return;
      ev.preventDefault();
      ev.stopImmediatePropagation();
      const text = ev.clipboardData?.getData("text/plain") ?? "";
      console.warn("[terminal] paste intercepted, len=", text.length, "target=", (ev.target as Element)?.tagName);
      if (text) this.term.paste(text);
    };
    document.addEventListener("paste", this.docPasteHandler, /* capture */ true);
    console.warn("[terminal] paste handler attached at document level");

    // Standard terminal-app keybindings:
    //   Ctrl+C → copy if text is selected; otherwise fall through (SIGINT)
    //   Ctrl+V → swallow the keydown so xterm doesn't translate it to a
    //            literal ^V byte (0x16). PSReadLine maps ^V to its own
    //            "PasteFromClipboard" action — combined with xterm's
    //            native paste-event handler that ALSO sends the clipboard
    //            text, you end up with two pastes. Letting only the
    //            browser-fired paste event reach xterm produces one paste.
    this.term.attachCustomKeyEventHandler(e => {
      if (e.type !== "keydown") return true;
      if (!e.ctrlKey || e.altKey || e.metaKey) return true;

      const isC = e.key === "c" || e.key === "C";
      const isV = e.key === "v" || e.key === "V";

      if (isV) return false; // suppress ^V; native paste event will deliver

      if (isC) {
        const sel = this.term.getSelection();
        if (sel && (sel.length > 0 || e.shiftKey)) {
          navigator.clipboard.writeText(sel).catch(err =>
            console.warn("[terminal] clipboard.writeText failed:", err));
          return false; // consume — don't send ^C
        }
        // No selection (and not Ctrl+Shift+C): let xterm send SIGINT.
        return true;
      }

      return true;
    });

    // xterm gives strings; convert to UTF-8 bytes for the C# side
    const enc = new TextEncoder();
    this.term.onData(s => {
      const bytes = enc.encode(s);
      // DIAGNOSTIC — remove once paste duplication is resolved.
      console.warn("[terminal] onData len=", bytes.length, "preview=", s.length > 32 ? s.slice(0, 32) + "..." + s.length : s);
      opts.onInput(bytes);
    });
    this.term.onResize(({ cols, rows }) => opts.onResize(cols, rows));
  }

  fitToContainer(): { cols: number; rows: number } {
    this.fit.fit();
    return { cols: this.term.cols, rows: this.term.rows };
  }

  writeBytes(bytes: Uint8Array): void {
    this.term.write(bytes);
  }

  setTheme(theme: ThemeName): void {
    this.term.options.theme = XtermThemes[theme];
  }

  focus(): void { this.term.focus(); }

  dispose(): void {
    if (this.docPasteHandler) {
      document.removeEventListener("paste", this.docPasteHandler, /* capture */ true);
      this.docPasteHandler = undefined;
    }
    this.term.dispose();
    this.host.remove();
  }
}
