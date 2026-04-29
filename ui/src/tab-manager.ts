import { Bridge, encodeBase64, decodeBase64 } from "./bridge.js";
import { XtermTab } from "./xterm-tab.js";

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

  constructor(
    private bridge: Bridge,
    private tabsContainer: HTMLDivElement,
    private terminalsContainer: HTMLDivElement,
  ) {
    bridge.on("tabsList", (d: { tabs: { tabId: string; title: string; alive: boolean }[] }) => {
      // On reattach: rebuild tabs we don't have yet, then request replay for each.
      for (const t of d.tabs) {
        if (!this.tabs.has(t.tabId)) this.attachExistingTab(t.tabId, t.title);
        this.bridge.send("replay", { tabId: t.tabId });
      }
      if (d.tabs.length > 0 && !this.activeTabId) this.activate(d.tabs[0]!.tabId);
    });

    bridge.on("tabCreated", (d: { tabId: string; title: string }) => {
      this.attachExistingTab(d.tabId, d.title);
      this.activate(d.tabId);
    });

    bridge.on("tabClosed", (d: { tabId: string }) => this.removeTab(d.tabId));

    bridge.on("output", (d: { tabId: string; dataB64: string }) => {
      this.tabs.get(d.tabId)?.xterm.writeBytes(decodeBase64(d.dataB64));
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

  setScrollbackLines(n: number) { this.scrollbackLines = n; }

  newTab(): void {
    // Build a temporary xterm to measure cols/rows for the host viewport
    const probe = new XtermTab({
      scrollbackLines: 100,
      onInput: () => {},
      onResize: () => {},
    });
    this.terminalsContainer.appendChild(probe.host);
    probe.host.classList.add("active");
    const { cols, rows } = probe.fitToContainer();
    probe.dispose();
    this.bridge.send("createTab", { cols, rows });
  }

  closeActiveTab(): void {
    if (this.activeTabId) this.bridge.send("closeTab", { tabId: this.activeTabId });
  }

  private attachExistingTab(tabId: string, title: string) {
    const xterm = new XtermTab({
      scrollbackLines: this.scrollbackLines,
      onInput: bytes => this.bridge.send("input", { tabId, dataB64: encodeBase64(bytes) }),
      onResize: (cols, rows) => this.bridge.send("resize", { tabId, cols, rows }),
    });
    this.terminalsContainer.appendChild(xterm.host);

    const tabEl = document.createElement("div");
    tabEl.className = "tab";
    tabEl.innerHTML = `<span class="tab-title">${title}</span><span class="tab-close">×</span>`;
    tabEl.addEventListener("click", e => {
      if ((e.target as HTMLElement).classList.contains("tab-close")) {
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
