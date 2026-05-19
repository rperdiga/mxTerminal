import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import { WebLinksAddon } from "@xterm/addon-web-links";
import xtermCss from "@xterm/xterm/css/xterm.css";
import { ThemeName, XtermThemes, resolveTheme } from "./theme.js";
import { showNotice } from "./notice.js";
import {
  classifyPasteSize,
  countLines,
  estimatePasteDurationMs,
  extractClipboardImage,
  normalizePasteLineEndings,
} from "./paste.js";

// Paced-rate constants used to estimate user-visible paste duration. Must
// match tab-manager.ts PACED_CHUNK_BYTES / PACED_CHUNK_DELAY_MS. Inlined
// here (rather than imported) so xterm-tab and tab-manager stay
// independently importable for tests.
const PACED_RATE_CHUNK_BYTES = 256;
const PACED_RATE_DELAY_MS = 25;

// Hard cap for image paste (raw bytes, pre-base64). 25 MB covers an 8K
// screenshot with headroom and prevents a clipboard-bomb from filling
// disk. Enforced JS-side AND C#-side as defense in depth.
const IMAGE_PASTE_MAX_BYTES = 25 * 1024 * 1024;

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
  /**
   * Called when the user pastes raw image bytes from the clipboard (e.g. a
   * screenshot). The owner (TabManager) base64-encodes and sends a
   * "paste-image" bridge message with the tabId attached. When unset, image
   * pastes fall back to the text path (which usually means "do nothing" —
   * the clipboard text is empty for image-only pastes).
   */
  onPasteImage?: (mime: string, bytes: Uint8Array, nameHint: string | null) => void;
  /** Diagnostic sink — writes to the C# log via the bridge. Avoids
   *  DevTools-filter quirks for things we want logged regardless. */
  diag?: (msg: string) => void;
}

export class XtermTab {
  readonly host: HTMLDivElement;
  private term: Terminal;
  private fit: FitAddon;
  private docPasteHandler?: (ev: ClipboardEvent) => void;
  private windowKeyHandler?: (ev: KeyboardEvent) => void;
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

    // WKWebView (Mendix Studio Pro on macOS) won't grant keyboard focus to
    // xterm's hidden textarea — by default xterm puts it at top:-9999em off-
    // screen, and WebKit refuses programmatic focus() on elements that are
    // outside the viewport. Result: the user can click, the focus call runs,
    // but no keyboard event ever fires. WebView2 on Windows is permissive and
    // doesn't have this restriction. Two-part fix:
    //   1. After term.open, find xterm's helper textarea and force it to a
    //      tiny on-screen position with opacity 0 (focusable, invisible).
    //   2. mousedown on the host walks the focus chain explicitly so WebKit's
    //      "user gesture" policy is satisfied for the programmatic focus.
    const textarea = this.host.querySelector(
      ".xterm-helper-textarea",
    ) as HTMLTextAreaElement | null;
    if (textarea) {
      textarea.style.position = "absolute";
      textarea.style.left = "0";
      textarea.style.top = "0";
      textarea.style.width = "1px";
      textarea.style.height = "1px";
      textarea.style.opacity = "0";
      textarea.style.pointerEvents = "none";
      textarea.style.zIndex = "-1";
    }
    this.host.addEventListener("mousedown", () => {
      textarea?.focus();
      this.term.focus();
    });
    // First-show focus: when the pane mounts, the user hasn't clicked yet but
    // expects to type immediately. Schedule on the next microtask so xterm's
    // own ready-state has settled.
    queueMicrotask(() => {
      textarea?.focus();
      this.term.focus();
    });

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
      const cd = ev.clipboardData;

      // Image branch — prefer image when clipboard carries both image and text.
      // Reads File via DataTransferItem.getAsFile, base64-encodes through the
      // bridge, and hands off to the C# side which writes a temp file and
      // injects the path. See docs/superpowers/specs/2026-05-18-image-paste-temp-path-injection-design.md
      const image = extractClipboardImage(cd);
      if (image && opts.onPasteImage) {
        const sizeBytes = image.file.size;
        if (sizeBytes > IMAGE_PASTE_MAX_BYTES) {
          showNotice(
            "err",
            `Image too large to paste (${(sizeBytes / 1024 / 1024).toFixed(1)} MB > ${IMAGE_PASTE_MAX_BYTES / 1024 / 1024} MB limit).`,
            15000,
          );
          this.diag(`paste-image rejected: ${sizeBytes} bytes > cap`);
          return;
        }
        this.diag(
          `paste-image mime=${image.mime} bytes=${sizeBytes} name=${image.file.name || "(none)"}`,
        );
        // File.arrayBuffer() returns a Promise; we don't await inside the
        // synchronous paste handler — fire-and-forget is safe because nothing
        // else in the handler runs after the early return.
        image.file
          .arrayBuffer()
          .then((buf) => {
            const bytes = new Uint8Array(buf);
            opts.onPasteImage!(image.mime, bytes, image.file.name || null);
          })
          .catch((err) => {
            this.diag(`paste-image read failed: ${err}`);
            showNotice("err", `Couldn't read pasted image: ${err}`, 8000);
          });
        return;
      }

      // Text branch (unchanged from prior behavior).
      const text = cd?.getData("text/plain") ?? "";
      const bracketed =
        (this.term as unknown as { modes?: { bracketedPasteMode?: boolean } })
          .modes?.bracketedPasteMode ?? "unknown";
      const types = cd ? Array.from(cd.types) : [];
      this.diag(
        `paste bracketed=${bracketed} types=[${types.join(",")}] plainLen=${text.length}`,
      );
      if (!text) return;

      const bracketedActive =
        (this.term as unknown as { modes?: { bracketedPasteMode?: boolean } })
          .modes?.bracketedPasteMode === true;
      const hasNewline = /\n|\r/.test(text);

      const byteLen = new TextEncoder().encode(text).length;
      const lineCount = countLines(text);
      const tier = classifyPasteSize(byteLen);
      if (tier === "block") {
        showNotice(
          "err",
          `Paste too large (${(byteLen / 1024).toFixed(0)} KB). Save to a file and use a 'read this file' command instead.`,
          15000,
        );
        return;
      }
      if (tier === "warn") {
        const seconds = Math.ceil(
          estimatePasteDurationMs(
            byteLen,
            PACED_RATE_CHUNK_BYTES,
            PACED_RATE_DELAY_MS,
          ) / 1000,
        );
        showNotice(
          "info",
          `Pasting ${(byteLen / 1024).toFixed(0)} KB / ${lineCount} lines (~${seconds}s). Some CLIs may have their own input limits.`,
          Math.max(8000, seconds * 1000 + 2000),
        );
      } else if (tier === "notice") {
        showNotice(
          "info",
          `Pasting ${lineCount} lines (${(byteLen / 1024).toFixed(1)} KB).`,
          4000,
        );
      }

      // xterm's term.paste() collapses \r?\n into bare \r when bracketed-paste
      // is off; line-aware CLIs (Claude Code, vim, multi-line PSReadLine) treat
      // bare CR as Enter and submit each line separately (Teams paste regression
      // observed 2026-05-01). Send LF via onInput instead.
      if (!bracketedActive && hasNewline) {
        const bytes = new TextEncoder().encode(normalizePasteLineEndings(text));
        this.diag(
          `paste bypass-CR-coercion len=${bytes.length} (bracketed mode off + multi-line)`,
        );
        opts.onInput(bytes);
        return;
      }

      this.term.paste(text);
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

    // WKWebView (Studio Pro on macOS) bypass — when the host doesn't grant
    // first-responder status to the WKWebView's content, xterm's hidden
    // textarea never receives keydown events even though clicks reach the
    // canvas. We capture keydown at the document level (capture phase) and
    // map to terminal byte sequences ourselves, routing through the same
    // onInput channel as xterm.onData. Active only when this tab's host is
    // marked .active by TabManager — multiple XtermTab instances coexist
    // safely. Skip when an input/textarea/select is focused so the settings
    // modal still works.
    this.windowKeyHandler = (e: KeyboardEvent) => {
      if (!this.host.classList.contains("active")) return;
      const a = document.activeElement;
      // If ANY input/textarea/select has focus, the normal browser path handles
      // it (xterm's helper textarea or the settings modal). Only fire when no
      // native input element holds focus — that's the WKWebView failure mode
      // we're patching. Without this guard, every keystroke is delivered twice
      // when focus IS working.
      if (
        a &&
        (a.tagName === "INPUT" || a.tagName === "TEXTAREA" || a.tagName === "SELECT")
      ) {
        return;
      }
      const bytes = keyEventToTerminalBytes(e);
      if (!bytes) return;
      e.preventDefault();
      this.diag(`win-keydown key=${e.key.length === 1 ? "*" : e.key} bytes=${bytes.length}`);
      opts.onInput(bytes);
    };
    document.addEventListener("keydown", this.windowKeyHandler, /* capture */ true);

    this.term.onData((s) => {
      const bytes = enc.encode(s);
      // Only log non-keystroke inputs (multi-byte sequences, paste residue).
      // Single-character keystrokes flood the log with one entry per keypress
      // and never preview content (secrets risk).
      if (bytes.length > 4) {
        this.diag(`onData len=${bytes.length}`);
      }
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
    if (this.windowKeyHandler) {
      document.removeEventListener(
        "keydown",
        this.windowKeyHandler,
        /* capture */ true,
      );
      this.windowKeyHandler = undefined;
    }
    this.term.dispose();
    this.host.remove();
  }
}

/**
 * Convert a DOM KeyboardEvent into the byte sequence a Unix-style terminal
 * would receive. Used as a fallback path on macOS WKWebView where xterm's
 * hidden textarea never receives focus and its built-in keymap can't fire.
 *
 * Mapping is the standard VT100/xterm vocabulary:
 *   - Printable: UTF-8 bytes of the character
 *   - Enter / Tab / Backspace / Esc: 0x0D, 0x09, 0x7F, 0x1B
 *   - Arrows / Home / End / PgUp / PgDn / Delete / Insert: ESC[ … sequences
 *   - F1-F4: ESC O P/Q/R/S, F5-F12: ESC [ <num> ~
 *   - Ctrl+letter: control character (Ctrl+C → 0x03, etc.)
 * Returns null for modifier-only presses or unsupported combos so the caller
 * doesn't preventDefault on keys it can't handle.
 */
function keyEventToTerminalBytes(e: KeyboardEvent): Uint8Array | null {
  // Modifier-only keys: skip
  if (e.key === "Control" || e.key === "Shift" || e.key === "Alt" || e.key === "Meta") return null;
  // Don't intercept system shortcuts (Cmd+Q, Cmd+W, etc.)
  if (e.metaKey) return null;

  const enc = new TextEncoder();

  switch (e.key) {
    case "Enter":     return new Uint8Array([0x0D]);
    case "Tab":       return new Uint8Array([0x09]);
    case "Backspace": return new Uint8Array([0x7F]);
    case "Escape":    return new Uint8Array([0x1B]);
    case "ArrowUp":    return enc.encode("\x1b[A");
    case "ArrowDown":  return enc.encode("\x1b[B");
    case "ArrowRight": return enc.encode("\x1b[C");
    case "ArrowLeft":  return enc.encode("\x1b[D");
    case "Home":       return enc.encode("\x1b[H");
    case "End":        return enc.encode("\x1b[F");
    case "PageUp":     return enc.encode("\x1b[5~");
    case "PageDown":   return enc.encode("\x1b[6~");
    case "Delete":     return enc.encode("\x1b[3~");
    case "Insert":     return enc.encode("\x1b[2~");
  }

  const fMatch = /^F([1-9]|1[0-2])$/.exec(e.key);
  if (fMatch) {
    const n = parseInt(fMatch[1]!, 10);
    if (n <= 4) return enc.encode(`\x1bO${"PQRS"[n - 1]}`);
    const codes = [15, 17, 18, 19, 20, 21, 23, 24];
    return enc.encode(`\x1b[${codes[n - 5]}~`);
  }

  // Ctrl+letter (Ctrl+A=0x01, …, Ctrl+Z=0x1A)
  if (e.ctrlKey && !e.altKey && e.key.length === 1) {
    const c = e.key.toLowerCase().charCodeAt(0);
    if (c >= 0x61 && c <= 0x7a) return new Uint8Array([c - 0x60]);
    // Ctrl+Space → NUL
    if (e.key === " ") return new Uint8Array([0x00]);
  }

  // Printable single character
  if (!e.ctrlKey && !e.altKey && e.key.length === 1) {
    return enc.encode(e.key);
  }

  // Alt+letter → ESC + letter (meta-prefixed input convention)
  if (e.altKey && !e.ctrlKey && e.key.length === 1) {
    const buf = new Uint8Array(2);
    buf[0] = 0x1b;
    buf[1] = e.key.charCodeAt(0);
    return buf;
  }

  return null;
}
