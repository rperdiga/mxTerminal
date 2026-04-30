import { Bridge } from "./bridge.js";
import { ThemeName, applyBodyTheme, isThemeName } from "./theme.js";

interface ShellOption { name: string; path: string; }

interface SettingsPayload {
  shellPath: string;
  args: string[];
  ringBufferKB: number;
  xtermScrollbackLines: number;
  theme: string;
  availableShells: ShellOption[];
  mcpEnabled: boolean;
  mcpPort: number;
  mcpClients: string[];
}

interface McpResult {
  ok: boolean;
  message: string;
  touched: string[];
}

const CUSTOM_VALUE = "__custom__";

export class SettingsModal {
  private modal = document.getElementById("settings-modal") as HTMLDivElement;
  private selShell = document.getElementById("set-shell-select") as HTMLSelectElement;
  private inpShell = document.getElementById("set-shell") as HTMLInputElement;
  private rowShellCustom = document.getElementById("set-shell-custom-row") as HTMLDivElement;
  private inpArgs = document.getElementById("set-args") as HTMLInputElement;
  private selTheme = document.getElementById("set-theme") as HTMLSelectElement;
  private inpRing = document.getElementById("set-ring") as HTMLInputElement;
  private inpScroll = document.getElementById("set-scroll") as HTMLInputElement;
  private chkMcp = document.getElementById("set-mcp-enabled") as HTMLInputElement;
  private inpMcpPort = document.getElementById("set-mcp-port") as HTMLInputElement;
  private chkMcpClaude = document.getElementById("set-mcp-claude") as HTMLInputElement;
  private chkMcpCopilot = document.getElementById("set-mcp-copilot") as HTMLInputElement;
  private chkMcpCodex = document.getElementById("set-mcp-codex") as HTMLInputElement;
  private banner = document.getElementById("banner") as HTMLDivElement;
  private bannerIcon = document.getElementById("banner-icon") as HTMLSpanElement;
  private bannerMessage = document.getElementById("banner-message") as HTMLSpanElement;
  private bannerClose = document.getElementById("banner-close") as HTMLSpanElement;
  private bannerTimer: number | undefined;

  private knownShells: ShellOption[] = [];

  constructor(
    private bridge: Bridge,
    private onScrollbackChanged: (lines: number) => void,
    private onThemeChanged: (theme: ThemeName) => void,
  ) {
    document.getElementById("btn-settings")!.addEventListener("click", () => this.open());
    document.getElementById("set-cancel")!.addEventListener("click", () => this.close());
    document.getElementById("set-save")!.addEventListener("click", () => this.save());

    this.selShell.addEventListener("change", () => this.onShellSelectChange());
    this.chkMcp.addEventListener("change", () => this.onMcpEnabledChange());

    bridge.on("settings", (d: SettingsPayload) => this.populate(d));
    bridge.on("mcpResult", (d: McpResult) => this.showBanner(d.ok ? "ok" : "err", d.message));
  }

  /** When the master MCP toggle flips, sync the per-CLI checkboxes:
   *  - turning OFF unchecks all three (and disables them)
   *  - turning ON re-enables them (leaves their last values alone) */
  private onMcpEnabledChange() {
    const enabled = this.chkMcp.checked;
    if (!enabled) {
      this.chkMcpClaude.checked = false;
      this.chkMcpCopilot.checked = false;
      this.chkMcpCodex.checked = false;
    }
    this.chkMcpClaude.disabled = !enabled;
    this.chkMcpCopilot.disabled = !enabled;
    this.chkMcpCodex.disabled = !enabled;
    this.inpMcpPort.disabled = !enabled;
  }

  private showBanner(kind: "ok" | "err", message: string) {
    this.banner.textContent = message;
    this.banner.className = `visible ${kind}`;
    if (this.bannerTimer !== undefined) window.clearTimeout(this.bannerTimer);
    this.bannerTimer = window.setTimeout(() => {
      this.banner.classList.remove("visible");
    }, kind === "ok" ? 5000 : 9000);
  }

  open() {
    this.bridge.send("openSettings");
    this.modal.classList.add("visible");
  }

  close() { this.modal.classList.remove("visible"); }

  /** Apply incoming settings to all form fields and to the live UI. */
  private populate(d: SettingsPayload) {
    this.knownShells = d.availableShells ?? [];
    this.rebuildShellSelect(d.shellPath);
    this.inpShell.value = d.shellPath;
    this.inpArgs.value = (d.args ?? []).join(" ");
    this.inpRing.value = String(d.ringBufferKB);
    this.inpScroll.value = String(d.xtermScrollbackLines);

    const theme = isThemeName(d.theme) ? d.theme : "dark";
    this.selTheme.value = theme;
    applyBodyTheme(theme);
    this.onThemeChanged(theme);
    this.onScrollbackChanged(d.xtermScrollbackLines);

    // MCP fields
    this.chkMcp.checked = !!d.mcpEnabled;
    this.inpMcpPort.value = String(d.mcpPort ?? 7782);
    const clients = new Set((d.mcpClients ?? []).map(c => c.toLowerCase()));
    this.chkMcpClaude.checked  = clients.has("claude");
    this.chkMcpCopilot.checked = clients.has("copilot");
    this.chkMcpCodex.checked   = clients.has("codex");
    // Apply enabled/disabled to children based on master state.
    this.onMcpEnabledChange();
  }

  private rebuildShellSelect(currentPath: string) {
    this.selShell.innerHTML = "";
    let matched = false;
    for (const s of this.knownShells) {
      const opt = document.createElement("option");
      opt.value = s.path;
      opt.textContent = `${s.name}  (${s.path})`;
      if (s.path.toLowerCase() === currentPath.toLowerCase()) {
        opt.selected = true;
        matched = true;
      }
      this.selShell.appendChild(opt);
    }
    const customOpt = document.createElement("option");
    customOpt.value = CUSTOM_VALUE;
    customOpt.textContent = "Custom…";
    if (!matched) customOpt.selected = true;
    this.selShell.appendChild(customOpt);

    // Show the custom path row only when "Custom…" is selected.
    this.rowShellCustom.classList.toggle("field-hidden", matched);
  }

  private onShellSelectChange() {
    const v = this.selShell.value;
    if (v === CUSTOM_VALUE) {
      this.rowShellCustom.classList.remove("field-hidden");
      // Keep whatever the user previously had; just expose the field.
    } else {
      this.rowShellCustom.classList.add("field-hidden");
      this.inpShell.value = v; // selected dropdown path becomes the canonical path
    }
  }

  private save() {
    const args = this.inpArgs.value.trim();
    const themeRaw = this.selTheme.value;
    const theme: ThemeName = isThemeName(themeRaw) ? themeRaw : "dark";
    const shellPath = this.selShell.value === CUSTOM_VALUE
      ? (this.inpShell.value.trim() || "powershell.exe")
      : this.selShell.value;

    const mcpClients: string[] = [];
    if (this.chkMcpClaude.checked)  mcpClients.push("claude");
    if (this.chkMcpCopilot.checked) mcpClients.push("copilot");
    if (this.chkMcpCodex.checked)   mcpClients.push("codex");

    this.bridge.send("saveSettings", {
      shellPath,
      args: args ? args.split(/\s+/) : [],
      ringBufferKB: parseInt(this.inpRing.value, 10) || 4096,
      xtermScrollbackLines: parseInt(this.inpScroll.value, 10) || 10000,
      theme,
      mcpEnabled: this.chkMcp.checked,
      mcpPort: parseInt(this.inpMcpPort.value, 10) || 7782,
      mcpClients,
    });

    applyBodyTheme(theme);
    this.onThemeChanged(theme);
    this.onScrollbackChanged(parseInt(this.inpScroll.value, 10) || 10000);
    this.close();
  }
}
