import { Bridge, encodeBase64, decodeBase64 } from "./bridge.js";
import { XtermTab } from "./xterm-tab.js";
import { ThemeName, applyTheme, resolveTheme } from "./theme.js";
import { icon } from "./icons.js";
import { pasteChunkRanges } from "./paste.js";

interface TabState {
  tabId: string;
  title: string;
  xterm: XtermTab;
  tabEl: HTMLDivElement;
}

export class TabManager {
  private tabs = new Map<string, TabState>();
  private activeTabId: string | null = null;
  private scrollbackLines = 10000;
  // Initial value is "auto" so the FIRST xterm spawned (before settings come
  // back from C#) picks up the right theme via the URL ?theme= param. If
  // initial were "dark", a race could leave xterm rendering dark on a light
  // host until a theme-update forced refresh.
  private theme: ThemeName = "auto";

  constructor(
    private bridge: Bridge,
    private tabsContainer: HTMLDivElement,
    private terminalsContainer: HTMLDivElement,
  ) {
    bridge.on(
      "tabsList",
      (d: { tabs: { tabId: string; title: string; alive: boolean }[] }) => {
        // On reattach: rebuild tabs we don't have yet, then request replay for each.
        for (const t of d.tabs) {
          if (!this.tabs.has(t.tabId)) this.attachExistingTab(t.tabId, t.title);
          this.bridge.send("replay", { tabId: t.tabId });
        }
        if (d.tabs.length > 0 && !this.activeTabId)
          this.activate(d.tabs[0]!.tabId);
      },
    );

    bridge.on("tabCreated", (d: { tabId: string; title: string }) => {
      this.attachExistingTab(d.tabId, d.title);
      this.activate(d.tabId);
    });

    bridge.on("tabClosed", (d: { tabId: string }) => this.removeTab(d.tabId));

    bridge.on("output", (d: { tabId: string; dataB64: string }) => {
      const bytes = decodeBase64(d.dataB64);
      // Phase-1.5 telemetry — does the running CLI ever enable bracketed-paste mode?
      // ESC [ ? 2 0 0 4 h = 1B 5B 3F 32 30 30 34 68 (set), ...6C (reset).
      // Logged once per occurrence so we can correlate with subsequent paste events.
      this.scanForBracketedPasteToggle(d.tabId, bytes);
      this.tabs.get(d.tabId)?.xterm.writeBytes(bytes);
    });

    bridge.on("replayData", (d: { tabId: string; dataB64: string }) => {
      this.tabs.get(d.tabId)?.xterm.writeBytes(decodeBase64(d.dataB64));
    });

    bridge.on("exit", (d: { tabId: string; exitCode?: number }) => {
      const t = this.tabs.get(d.tabId);
      if (!t) return;
      t.tabEl.querySelector(".tab-title")!.textContent = `${t.title} (exited)`;
      t.tabEl.style.opacity = "0.6";
    });

    bridge.on("error", (d: { message: string; context?: string }) => {
      console.error("[terminal:error]", d.message, d.context);
    });

    window.addEventListener("resize", () => this.resizeActive());
  }

  setScrollbackLines(n: number) {
    this.scrollbackLines = n;
  }

  /**
   * Mendix's WebView postMessage has a per-message size limit (≈ 1 MB in
   * practice). A single Ctrl+V of a long string was arriving truncated on
   * the C# side. Split the bytes into 16 KB chunks and send each as its own
   * "input" message — C# processes them in arrival order so the PTY sees
   * the original byte stream intact.
   */
  private static readonly INPUT_CHUNK_BYTES = 16 * 1024;

  // Paced-paste tuning. When a paste lands in the input stream, slicing it
  // into small chunks with delays gives Node/Ink-based TUI agents (Claude
  // Code, Codex, Aider, Gemini CLI) time to drain their stdin tokenizer's
  // ring buffer between bursts. Without pacing, large pastes overrun the
  // buffer and lose chunks — claude-code #49337 / #50012 / #49673 / #50250.
  //
  // Tuning history:
  //   2026-05-01 17:30 — VS Code's tuned numbers (512B / 10ms). 5 chunks
  //     for a 2.4KB paste delivered in 47ms — Claude Code still dropped
  //     middle chunks. Symptom: "providedsufficient" mid-sentence merge
  //     (chunks 2+3 lost). Hypothesis: WinPTY's hidden conhost intermediate
  //     buffer is the bottleneck, not Node's stdin tokenizer.
  //   2026-05-01 18:30 — Tightened to 256B / 25ms. 10 chunks for the same
  //     paste delivered in ~250ms (still imperceptible; well under any
  //     reasonable typing speed). Conservative until ConPTY migration
  //     replaces WinPTY entirely.
  //
  // Gate at 1KB so single-line typing and small pastes go through
  // unsliced (zero added latency for the common case).
  private static readonly PACED_CHUNK_THRESHOLD = 1024;
  private static readonly PACED_CHUNK_BYTES = 256;
  private static readonly PACED_CHUNK_DELAY_MS = 25;

  // Pattern: ESC [ ? 2 0 0 4 (h|l)  -- xterm DECSET/DECRST 2004 (bracketed paste).
  private static readonly BRACKET_SET = [
    0x1b, 0x5b, 0x3f, 0x32, 0x30, 0x30, 0x34, 0x68,
  ];
  private static readonly BRACKET_RESET = [
    0x1b, 0x5b, 0x3f, 0x32, 0x30, 0x30, 0x34, 0x6c,
  ];

  private scanForBracketedPasteToggle(tabId: string, bytes: Uint8Array): void {
    const has = (needle: number[]): boolean => {
      outer: for (let i = 0; i + needle.length <= bytes.length; i++) {
        for (let j = 0; j < needle.length; j++) {
          if (bytes[i + j] !== needle[j]) continue outer;
        }
        return true;
      }
      return false;
    };
    if (has(TabManager.BRACKET_SET)) {
      this.bridge.send("diag", {
        msg: `bracket-mode SET tab=${tabId.slice(0, 8)}`,
      });
    }
    if (has(TabManager.BRACKET_RESET)) {
      this.bridge.send("diag", {
        msg: `bracket-mode RESET tab=${tabId.slice(0, 8)}`,
      });
    }
  }

  private sendInputChunked(tabId: string, bytes: Uint8Array) {
    // Common case: small input (typing, single-line paste) — send unsliced.
    if (bytes.length < TabManager.PACED_CHUNK_THRESHOLD) {
      this.bridge.send("input", { tabId, dataB64: encodeBase64(bytes) });
      return;
    }
    // Paced path: slice into 512-byte chunks with 10ms gaps. Fire-and-forget
    // — we don't await this from the caller because the xterm onData callback
    // is sync and the receiver doesn't care when we finish, only that bytes
    // arrive in order. The C# WriteLock (per session) serializes per-tab so
    // the chunks can't interleave even though they arrive on separate bridge
    // dispatches.
    void this.sendPaced(tabId, bytes);
  }

  private async sendPaced(tabId: string, bytes: Uint8Array): Promise<void> {
    const total = bytes.length;
    const t0 = performance.now();
    let chunks = 0;
    let prior = false;
    for (const [off, end] of pasteChunkRanges(
      total,
      TabManager.PACED_CHUNK_BYTES,
    )) {
      if (prior) {
        await new Promise((r) =>
          setTimeout(r, TabManager.PACED_CHUNK_DELAY_MS),
        );
      }
      this.bridge.send("input", {
        tabId,
        dataB64: encodeBase64(bytes.subarray(off, end)),
      });
      chunks += 1;
      prior = true;
    }
    const dt = Math.round(performance.now() - t0);
    this.bridge.send("diag", {
      msg: `paced-input tab=${tabId.slice(0, 8)} bytes=${total} chunks=${chunks} elapsed=${dt}ms`,
    });
  }

  setTheme(theme: ThemeName): void {
    this.theme = theme;
    // Single source of truth: apply body theme (resolves "auto") and
    // live-update every existing xterm instance.
    const resolved = applyTheme(theme);
    const mq =
      typeof window !== "undefined" && window.matchMedia
        ? `pcs-dark=${window.matchMedia("(prefers-color-scheme: dark)").matches}`
        : "no-matchMedia";
    this.bridge.send("diag", {
      msg: `setTheme setting=${theme} resolved=${resolved} ${mq} html.class=${document.documentElement.className}`,
    });
    void resolveTheme; // keep import alive across future refactors
    for (const t of this.tabs.values()) t.xterm.setTheme(theme);
  }

  getTheme(): ThemeName {
    return this.theme;
  }

  newTab(): void {
    // Spawn at sensible defaults — the fit-addon on the real xterm will
    // resize the PTY to actual viewport size as soon as it mounts. The
    // earlier "probe a throwaway xterm to measure dimensions" approach
    // triggered an xterm RenderService race ("Cannot read properties of
    // undefined (reading 'dimensions')") because xterm.open() was called
    // on a host element that wasn't in the DOM yet.
    this.bridge.send("createTab", { cols: 80, rows: 24 });
  }

  closeActiveTab(): void {
    if (this.activeTabId)
      this.bridge.send("closeTab", { tabId: this.activeTabId });
  }

  private attachExistingTab(tabId: string, title: string) {
    // Create host element and attach to DOM FIRST so it has dimensions,
    // THEN construct XtermTab which will call term.open() on a properly
    // sized container. The reverse order triggers an xterm RenderService
    // race because term.open() on a detached host gives zero dimensions.
    const host = document.createElement("div");
    host.className = "terminal-host active"; // start active so it's measurable
    this.terminalsContainer.appendChild(host);

    const xterm = new XtermTab({
      host,
      scrollbackLines: this.scrollbackLines,
      theme: this.theme,
      onInput: (bytes) => this.sendInputChunked(tabId, bytes),
      onResize: (cols, rows) =>
        this.bridge.send("resize", { tabId, cols, rows }),
      onPasteImage: (mime, bytes, nameHint) =>
        this.bridge.send("paste-image", {
          tabId,
          mime,
          bytesB64: encodeBase64(bytes),
          nameHint,
        }),
      diag: (msg) => this.bridge.send("diag", { msg }),
    });

    const tabEl = document.createElement("div");
    tabEl.className = "tab";
    // Title is text-content, not innerHTML, so it's safe against shell-name
    // characters that could form HTML. Close uses a Lucide x SVG.
    const titleEl = document.createElement("span");
    titleEl.className = "tab-title";
    titleEl.textContent = title;
    const closeEl = document.createElement("span");
    closeEl.className = "tab-close";
    closeEl.setAttribute("role", "button");
    closeEl.setAttribute("aria-label", "Close tab");
    closeEl.innerHTML = icon("x");
    tabEl.append(titleEl, closeEl);
    tabEl.addEventListener("click", (e) => {
      if ((e.target as HTMLElement).closest(".tab-close")) {
        this.bridge.send("closeTab", { tabId });
      } else {
        this.activate(tabId);
      }
    });
    this.tabsContainer.appendChild(tabEl);

    this.tabs.set(tabId, { tabId, title, xterm, tabEl });
  }

  private removeTab(tabId: string) {
    const t = this.tabs.get(tabId);
    if (!t) return;
    t.xterm.dispose();
    t.tabEl.remove();
    this.tabs.delete(tabId);
    if (this.activeTabId === tabId) {
      this.activeTabId = null;
      const next = this.tabs.keys().next().value;
      if (next) this.activate(next);
    }
  }

  private activate(tabId: string) {
    if (this.activeTabId === tabId) return;
    this.activeTabId = tabId;
    for (const [id, t] of this.tabs) {
      t.tabEl.classList.toggle("active", id === tabId);
      t.xterm.host.classList.toggle("active", id === tabId);
    }
    const t = this.tabs.get(tabId);
    if (t) {
      const { cols, rows } = t.xterm.fitToContainer();
      this.bridge.send("resize", { tabId, cols, rows });
      t.xterm.focus();
    }
  }

  private resizeActive() {
    if (!this.activeTabId) return;
    const t = this.tabs.get(this.activeTabId);
    if (!t) return;
    const { cols, rows } = t.xterm.fitToContainer();
    this.bridge.send("resize", { tabId: this.activeTabId, cols, rows });
  }
}
