import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import { WebLinksAddon } from "@xterm/addon-web-links";
import xtermCss from "@xterm/xterm/css/xterm.css";
import { ThemeName, XtermThemes, resolveTheme } from "./theme.js";

let cssInjected = false;
function ensureCssInjected() {
  if (cssInjected) return;
  const style = document.createElement("style");
  // Append our overrides AFTER xterm's stylesheet so cascade order favors them.
  // Thinner scrollbar reduces the visual collision with TUI box-drawing borders
  // (e.g. Claude Code's welcome card) that hug the right edge of the canvas.
  style.textContent =
    xtermCss +
    `
.xterm-viewport::-webkit-scrollbar { width: 8px; height: 8px; }
.xterm-viewport::-webkit-scrollbar-track { background: transparent; }
.xterm-viewport::-webkit-scrollbar-thumb { background: rgba(128,128,128,0.35); border-radius: 4px; }
.xterm-viewport::-webkit-scrollbar-thumb:hover { background: rgba(128,128,128,0.55); }
`;
  document.head.appendChild(style);
  cssInjected = true;
}

export interface XtermTabOptions {
  /** Pre-attached host element. Must already be in the DOM with non-zero
   *  dimensions when XtermTab is constructed; xterm.open() needs the host
   *  to have a layout box so its RenderService can initialize. */
  host: HTMLDivElement;
  scrollbackLines: number;
  theme: ThemeName;
  onInput: (bytes: Uint8Array) => void;
  onResize: (cols: number, rows: number) => void;
  /** Diagnostic sink — writes to the C# log via the bridge. Avoids
   *  DevTools-filter quirks for things we want logged regardless. */
  diag?: (msg: string) => void;
}

export class XtermTab {
  readonly host: HTMLDivElement;
  private term: Terminal;
  private fit: FitAddon;
  private docPasteHandler?: (ev: ClipboardEvent) => void;
  private diag: (msg: string) => void;

  constructor(opts: XtermTabOptions) {
    ensureCssInjected();
    this.host = opts.host;
    this.diag = opts.diag ?? (() => {});

    this.term = new Terminal({
      scrollback: opts.scrollbackLines,
      // Cascadia Mono is a variable font shipping weights 200-700. We pull
      // it at weight 300 (Light) for a designer-clean look that reads less
      // "code editor brutalist" while still being a true monospace (terminal
      // grids must stay aligned). Size bumped 13→14 + lineHeight 1.3 + slight
      // letter-spacing for breathing room — closer to Studio Pro's chrome
      // typography while keeping monospace behavior PowerShell needs.
      fontFamily: "Cascadia Mono, Consolas, 'Courier New', monospace",
      fontSize: 14,
      fontWeight: "300",
      fontWeightBold: "600",
      lineHeight: 1.3,
      letterSpacing: 0.4,
      cursorStyle: "bar",
      cursorBlink: true,
      bellStyle: "none",
      theme: XtermThemes[resolveTheme(opts.theme)],
      allowProposedApi: true,
    });
    this.fit = new FitAddon();
    this.term.loadAddon(this.fit);
    this.term.loadAddon(new WebLinksAddon());
    this.term.open(this.host);

    // Paste interceptor — attached at document level with capture: true so
    // it runs BEFORE any other paste listener anywhere in the DOM (including
    // xterm's own listener and any default browser text-insertion that
    // fires an `input` event xterm interprets as a second submission).
    //
    // Only acts when the focused element is inside our host so we don't
    // hijack pastes meant for other UI sharing the WebView.
    this.docPasteHandler = (ev: ClipboardEvent) => {
      const focused = document.activeElement;
      if (!focused || !this.host.contains(focused)) return;
      ev.preventDefault();
      ev.stopImmediatePropagation();
      const text = ev.clipboardData?.getData("text/plain") ?? "";
      this.diag(
        `paste intercepted len=${text.length} target=${(ev.target as Element)?.tagName}`,
      );
      if (text) this.term.paste(text);
    };
    document.addEventListener(
      "paste",
      this.docPasteHandler,
      /* capture */ true,
    );
    this.diag("paste handler attached at document level");

    // Standard terminal-app keybindings:
    //   Ctrl+C → copy if text is selected; otherwise fall through (SIGINT)
    //   Ctrl+V → swallow the keydown so xterm doesn't translate it to a
    //            literal ^V byte (0x16) which would be a second paste path.
    this.term.attachCustomKeyEventHandler((e) => {
      if (e.type !== "keydown") return true;
      if (!e.ctrlKey || e.altKey || e.metaKey) return true;

      const isC = e.key === "c" || e.key === "C";
      const isV = e.key === "v" || e.key === "V";

      if (isV) return false; // suppress ^V; native paste event will deliver

      if (isC) {
        const sel = this.term.getSelection();
        if (sel && (sel.length > 0 || e.shiftKey)) {
          navigator.clipboard
            .writeText(sel)
            .catch((err) => this.diag(`clipboard.writeText failed: ${err}`));
          return false; // consume — don't send ^C
        }
        // No selection (and not Ctrl+Shift+C): let xterm send SIGINT.
        return true;
      }

      return true;
    });

    // xterm gives strings; convert to UTF-8 bytes for the C# side
    const enc = new TextEncoder();
    this.term.onData((s) => {
      const bytes = enc.encode(s);
      this.diag(
        `onData len=${bytes.length} preview=${s.length > 32 ? s.slice(0, 32) + "..." : s}`,
      );
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
    this.term.options.theme = XtermThemes[resolveTheme(theme)];
    // Force a redraw — xterm.js updates internal style state when options.theme
    // is reassigned, but the canvas/webgl renderer keeps the previous frame's
    // background fill until the next render pass. refresh() blits a fresh frame.
    this.term.refresh(0, this.term.rows - 1);
  }

  focus(): void {
    this.term.focus();
  }

  dispose(): void {
    if (this.docPasteHandler) {
      document.removeEventListener(
        "paste",
        this.docPasteHandler,
        /* capture */ true,
      );
      this.docPasteHandler = undefined;
    }
    this.term.dispose();
    this.host.remove();
  }
}
