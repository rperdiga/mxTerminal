import { Bridge } from "./bridge.js";
import { ThemeName } from "./theme.js";
import { mountIcon, IconName } from "./icons.js";
import { showNotice, hideNotice } from "./notice.js";
import { mountLogo } from "./logo.js";

const SECTIONS = [
  "general",
  "shell",
  "mcp",
  "actions",
  "skills",
  "about",
] as const;
type SectionName = (typeof SECTIONS)[number];

interface ShellOption {
  name: string;
  path: string;
}

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
  actionsServerEnabled: boolean;
  actionsServerPort: number;
  refreshFromDiskHotkey: string;
  restoreTabsOnReopen: boolean;
  about: AboutInfo;
  studioProMcp: StudioProMcpInfo | null;
}

interface StudioProMcpInfo {
  enabled: boolean | null;
  port: number | null;
}

interface AboutInfo {
  version: string;
  logPath: string | null;
  settingsPath: string | null;
}

interface McpResult {
  ok: boolean;
  message: string;
  touched: string[];
}

const CUSTOM_VALUE = "__custom__";

export class SettingsModal {
  private modal = document.getElementById("settings-modal") as HTMLDivElement;
  private selShell = document.getElementById(
    "set-shell-select",
  ) as HTMLSelectElement;
  private inpShell = document.getElementById("set-shell") as HTMLInputElement;
  private rowShellCustom = document.getElementById(
    "set-shell-custom-row",
  ) as HTMLDivElement;
  private inpArgs = document.getElementById("set-args") as HTMLInputElement;
  private inpRing = document.getElementById("set-ring") as HTMLInputElement;
  private inpScroll = document.getElementById("set-scroll") as HTMLInputElement;
  private chkRestoreTabs = document.getElementById(
    "set-restore-tabs",
  ) as HTMLInputElement;
  private chkMcp = document.getElementById(
    "set-mcp-enabled",
  ) as HTMLInputElement;
  // Port readout — read-only; URL written into .mcp.json always tracks
  // Studio Pro's actual port via the SQLite probe.
  private mcpPortReadout = document.getElementById(
    "mcp-port-readout",
  ) as HTMLDivElement;
  private chkMcpClaude = document.getElementById(
    "set-mcp-claude",
  ) as HTMLInputElement;
  private chkMcpCopilot = document.getElementById(
    "set-mcp-copilot",
  ) as HTMLInputElement;
  private chkMcpCodex = document.getElementById(
    "set-mcp-codex",
  ) as HTMLInputElement;
  private chkActions = document.getElementById(
    "set-actions-enabled",
  ) as HTMLInputElement;
  // Read-only readout — bridge picks its own port at startup (default 7783;
  // auto-fallback to a free port on collision). User no longer chooses.
  private actionsPortReadout = document.getElementById(
    "actions-port-readout",
  ) as HTMLDivElement;
  private inpRefreshHotkey = document.getElementById(
    "set-refresh-hotkey",
  ) as HTMLInputElement;
  private bannerClose = document.getElementById(
    "banner-close",
  ) as HTMLSpanElement;

  private knownShells: ShellOption[] = [];

  constructor(
    private bridge: Bridge,
    private onScrollbackChanged: (lines: number) => void,
    private onThemeChanged: (theme: ThemeName) => void,
  ) {
    document
      .getElementById("btn-settings")!
      .addEventListener("click", () => this.open());
    document
      .getElementById("set-cancel")!
      .addEventListener("click", () => this.close());
    document
      .getElementById("set-save")!
      .addEventListener("click", () => this.save());

    this.selShell.addEventListener("change", () => this.onShellSelectChange());
    this.chkMcp.addEventListener("change", () => this.onMcpEnabledChange());
    this.chkActions.addEventListener("change", () =>
      this.onActionsEnabledChange(),
    );
    this.bannerClose.addEventListener("click", () => hideNotice());

    this.mountNavIcons();
    this.wireNavRail();
    this.mountRailLogo();
    this.mountAboutLogo();
    this.activateSection("general");

    bridge.on("settings", (d: SettingsPayload) => this.populate(d));
    bridge.on("mcpResult", (d: McpResult) =>
      showNotice(d.ok ? "ok" : "err", d.message),
    );
  }

  /** Replace each .nav-icon[data-icon=NAME] placeholder with the matching SVG.
   *  Same trick for the "Coming soon" Skills card (.coming-soon .icon[data-icon]). */
  private mountNavIcons(): void {
    document.querySelectorAll<HTMLElement>("[data-icon]").forEach((el) => {
      const name = el.dataset.icon as IconName | undefined;
      if (name) mountIcon(el, name);
    });
  }

  /** Mount the OneSource logo into the nav rail above the About item.
   *  Always visible while the modal is open. Hover-to-spin behavior. */
  private mountRailLogo(): void {
    const host = document.getElementById("rail-logo");
    if (host && host.childElementCount === 0) mountLogo(host, 0, true);
  }

  /** Mount the OneSource logo into the About-section credit lockup.
   *  Larger and accompanied by the CoE attribution text. */
  private mountAboutLogo(): void {
    const host = document.getElementById("about-logo");
    if (host && host.childElementCount === 0) mountLogo(host, 0, false);
  }

  /** Wire click + keyboard nav on the rail (ARIA tablist pattern). */
  private wireNavRail(): void {
    const items = Array.from(
      document.querySelectorAll<HTMLDivElement>(".nav-item[data-section]"),
    );
    items.forEach((el, idx) => {
      el.addEventListener("click", () => {
        const target = el.dataset.section as SectionName | undefined;
        if (target) this.activateSection(target);
      });
      el.addEventListener("keydown", (e: KeyboardEvent) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          el.click();
          return;
        }
        if (e.key === "ArrowDown" || e.key === "ArrowUp") {
          e.preventDefault();
          const dir = e.key === "ArrowDown" ? 1 : -1;
          const next = items[(idx + dir + items.length) % items.length];
          next?.focus();
        }
      });
    });
  }

  /** Switch the visible section + sync rail aria/active state + move focus. */
  private activateSection(name: SectionName): void {
    document
      .querySelectorAll<HTMLDivElement>(".nav-item[data-section]")
      .forEach((el) => {
        const matches = el.dataset.section === name;
        el.classList.toggle("active", matches);
        el.setAttribute("aria-selected", String(matches));
        el.tabIndex = matches ? 0 : -1;
      });
    document
      .querySelectorAll<HTMLElement>(".settings-section[data-section]")
      .forEach((el) => {
        el.classList.toggle("active", el.dataset.section === name);
      });
  }

  /** When the master MCP toggle flips, sync the per-CLI checkboxes:
   *  - turning OFF unchecks the per-CLI client list (and disables them)
   *  - turning ON re-enables them (leaves their last values alone)
   *  Action bridge is INDEPENDENT — it writes its own .mcp.json entry and
   *  doesn't need the primary MCP integration to be on. (Earlier code
   *  force-unchecked it; that was wrong and produced "checkbox checked but
   *  pill OFF" reports.) */
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
    // chkActions.disabled removed — action bridge is independent.
    this.onActionsEnabledChange();
  }

  private onActionsEnabledChange() {
    // No port input to disable anymore — bridge auto-binds when enabled.
  }

  open() {
    this.bridge.send("openSettings");
    this.modal.classList.add("visible");
  }

  close() {
    this.modal.classList.remove("visible");
  }

  /** Apply incoming settings to all form fields and to the live UI. */
  private populate(d: SettingsPayload) {
    this.knownShells = d.availableShells ?? [];
    this.rebuildShellSelect(d.shellPath);
    this.inpShell.value = d.shellPath;
    this.inpArgs.value = (d.args ?? []).join(" ");
    this.inpRing.value = String(d.ringBufferKB);
    this.inpScroll.value = String(d.xtermScrollbackLines);
    this.chkRestoreTabs.checked = d.restoreTabsOnReopen ?? true;

    // Theme is not user-settable here; C# pushes Studio Pro's actual theme
    // via ?theme= URL param, with prefers-color-scheme as fallback. We always
    // call onThemeChanged("auto") so resolveTheme() runs the priority chain.
    this.onThemeChanged("auto");
    this.onScrollbackChanged(d.xtermScrollbackLines);

    // MCP fields
    this.chkMcp.checked = !!d.mcpEnabled;
    this.renderMcpPortReadout(d.studioProMcp);
    const clients = new Set((d.mcpClients ?? []).map((c) => c.toLowerCase()));
    this.chkMcpClaude.checked = clients.has("claude");
    this.chkMcpCopilot.checked = clients.has("copilot");
    this.chkMcpCodex.checked = clients.has("codex");
    // Apply enabled/disabled to children based on master state.
    this.onMcpEnabledChange();

    // Actions server fields
    this.chkActions.checked = d.actionsServerEnabled;
    this.renderActionsPortReadout(d.actionsServerEnabled, d.actionsServerPort);
    this.inpRefreshHotkey.value = d.refreshFromDiskHotkey;
    this.onMcpEnabledChange(); // also flips actions enabled state

    // About section
    this.populateAbout(d.about);

    // (Port-mismatch banner + chrome status pill retired — the readouts
    // inside Settings → Studio Pro MCP and Settings → Action bridge are
    // the canonical state surfaces now.)
  }

  /** Read-only port readout under the Action bridge enable checkbox.
   *  Shows the live bound port (or "not running" when disabled). */
  private renderActionsPortReadout(enabled: boolean, boundPort: number): void {
    if (!this.actionsPortReadout) return;
    if (!enabled) {
      this.actionsPortReadout.classList.remove("warn");
      this.actionsPortReadout.innerHTML = `Action bridge is <strong>not running</strong>. Enable to start the local HTTP server that exposes Studio Pro UI actions to the CLIs above.`;
      return;
    }
    this.actionsPortReadout.classList.remove("warn");
    this.actionsPortReadout.innerHTML =
      `Action bridge is listening on <code>localhost:${boundPort}</code>. ` +
      `Each Save writes that URL into the CLI configs. Default is 7783; ` +
      `if that's busy on your machine the bridge falls back to a free port automatically.`;
  }

  /** Read-only port readout under the MCP enable checkbox. Surfaces what
   *  Studio Pro is actually serving on (or warns when it can't tell). */
  private renderMcpPortReadout(sp: StudioProMcpInfo | null): void {
    if (!this.mcpPortReadout) return;
    if (sp?.enabled === true && sp.port != null) {
      this.mcpPortReadout.classList.remove("warn");
      this.mcpPortReadout.innerHTML =
        `Studio Pro reports its MCP server on <code>localhost:${sp.port}</code>. ` +
        `Each Save writes that URL into the CLI configs below.`;
      return;
    }
    if (sp?.enabled === false) {
      this.mcpPortReadout.classList.add("warn");
      this.mcpPortReadout.innerHTML =
        `Studio Pro's MCP server is <strong>disabled</strong>. ` +
        `Enable it in <em>Edit → Preferences → Maia → MCP Server</em>, then reopen this pane.`;
      return;
    }
    // Probe couldn't tell — file unreadable, schema mismatch, etc.
    this.mcpPortReadout.classList.add("warn");
    this.mcpPortReadout.innerHTML =
      `Couldn't read Studio Pro's MCP preference. Check <em>Edit → Preferences → Maia → MCP Server</em> ` +
      `is enabled with a port set, then reopen this pane.`;
  }

  private populateAbout(a: AboutInfo | undefined): void {
    const set = (id: string, value: string) => {
      const el = document.getElementById(id);
      if (el) el.textContent = value;
    };
    set("about-version", a?.version ?? "—");
    set("about-log", a?.logPath ?? "—");
    set("about-settings", a?.settingsPath ?? "—");
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
    const theme: ThemeName = "auto";
    const shellPath =
      this.selShell.value === CUSTOM_VALUE
        ? this.inpShell.value.trim() || "powershell.exe"
        : this.selShell.value;

    const mcpClients: string[] = [];
    if (this.chkMcpClaude.checked) mcpClients.push("claude");
    if (this.chkMcpCopilot.checked) mcpClients.push("copilot");
    if (this.chkMcpCodex.checked) mcpClients.push("codex");

    this.bridge.send("saveSettings", {
      shellPath,
      args: args ? args.split(/\s+/) : [],
      ringBufferKB: parseInt(this.inpRing.value, 10) || 4096,
      xtermScrollbackLines: parseInt(this.inpScroll.value, 10) || 10000,
      theme,
      mcpEnabled: this.chkMcp.checked,
      // mcpPort intentionally omitted — C# always uses Studio Pro's actual
      // port (probed from Settings.sqlite). Saved field kept for back-compat
      // but no longer user-settable.
      mcpClients,
      actionsServerEnabled: this.chkActions.checked,
      // actionsServerPort intentionally omitted — bridge auto-binds default
      // 7783 with free-port fallback. Saved field kept for back-compat.
      refreshFromDiskHotkey: this.inpRefreshHotkey.value,
      restoreTabsOnReopen: this.chkRestoreTabs.checked,
    });

    this.onThemeChanged(theme);
    this.onScrollbackChanged(parseInt(this.inpScroll.value, 10) || 10000);
    this.close();
  }
}
