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

    // Standard terminal-app keybindings:
    //   Ctrl+C → copy if text is selected; otherwise fall through (SIGINT)
    //   Ctrl+V → handled by xterm's native paste event listener; we don't
    //            touch it here. (Manually calling term.paste() in addition
    //            to xterm's default produced double pastes.)
    this.term.attachCustomKeyEventHandler(e => {
      if (e.type !== "keydown") return true;
      if (!e.ctrlKey || e.altKey || e.metaKey) return true;

      const isC = e.key === "c" || e.key === "C";
      if (!isC) return true;

      const sel = this.term.getSelection();
      if (sel && (sel.length > 0 || e.shiftKey)) {
        navigator.clipboard.writeText(sel).catch(err =>
          console.warn("[terminal] clipboard.writeText failed:", err));
        return false; // consume — don't send ^C
      }
      // No selection (and not Ctrl+Shift+C): let xterm send SIGINT.
      return true;
    });

    // xterm gives strings; convert to UTF-8 bytes for the C# side
    const enc = new TextEncoder();
    this.term.onData(s => opts.onInput(enc.encode(s)));
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
    this.term.dispose();
    this.host.remove();
  }
}
