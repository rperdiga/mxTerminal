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
  mcpClients: string[];
  mcpServerEnabled: boolean;
  studioProActionsEnabled: boolean;
  maiaIntegrationEnabled: boolean;
  maiaDiagnosticLogging: boolean;
  // True iff the running Studio Pro version exposes the Maia AI panel that
  // Concord's bridge depends on (11.10+). When false, hide the Maia +
  // diagnostic checkboxes — they have no panel to act against on older
  // Studio Pros.
  maiaAvailable: boolean;
  platform: string;
  refreshFromDiskHotkey: string;
  restoreTabsOnReopen: boolean;
  about: AboutInfo;
  studioProMcp: StudioProMcpInfo | null;
  // Read-only display field. Live bound port of the Concord MCP server, or
  // null when not running. Never echoed back through saveSettings.
  liveActionServerPort: number | null;
  skillsEnabled: boolean;
  skillClients: string[];
  bundledSkills: BundledSkill[];
}

interface BundledSkill {
  name: string;
  description: string;
}

interface StudioProMcpInfo {
  enabled: boolean | null;
  port: number | null;
  // True iff the running Studio Pro version exposes the built-in
  // mendix-studio-pro MCP server (11.10+). When false, hide the entire
  // "Studio Pro MCP" section — the feature literally doesn't exist on
  // this Studio Pro version.
  available: boolean;
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
  private chkSpActions = document.getElementById(
    "set-sp-actions-enabled",
  ) as HTMLInputElement;
  private chkMaia = document.getElementById(
    "set-maia-enabled",
  ) as HTMLInputElement;
  private maiaPlatformNote = document.getElementById(
    "maia-platform-note",
  ) as HTMLDivElement;
  private chkMaiaDiagnostic = document.getElementById(
    "set-maia-diagnostic",
  ) as HTMLInputElement;
  private inpRefreshHotkey = document.getElementById(
    "set-refresh-hotkey",
  ) as HTMLInputElement;
  private chkSkillsEnabled = document.getElementById(
    "set-skills-enabled",
  ) as HTMLInputElement;
  private chkSkillsClaude = document.getElementById(
    "set-skills-claude",
  ) as HTMLInputElement;
  private chkSkillsCopilot = document.getElementById(
    "set-skills-copilot",
  ) as HTMLInputElement;
  private chkSkillsCodex = document.getElementById(
    "set-skills-codex",
  ) as HTMLInputElement;
  private bundledSkillsList = document.getElementById(
    "bundled-skills-list",
  ) as HTMLUListElement;
  private bannerClose = document.getElementById(
    "banner-close",
  ) as HTMLSpanElement;

  private knownShells: ShellOption[] = [];
  // Snapshot of whether the running Studio Pro version exposes the
  // mendix-studio-pro MCP server. Captured on every populate() so the save
  // path can defensively clamp McpEnabled + McpClients to off when the
  // section is hidden — even though populate already cleared the
  // checkboxes, this guards against stale form state.
  private studioProMcpAvailable: boolean = false;
  // Symmetric snapshot for the Maia AI panel. Same defensive purpose: when
  // false, save() must send maiaIntegrationEnabled + maiaDiagnosticLogging
  // as false even if the hidden checkboxes somehow have stale state.
  private maiaAvailable: boolean = false;

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
    this.chkSkillsEnabled.addEventListener("change", () =>
      this.onSkillsEnabledChange(),
    );
    this.bannerClose.addEventListener("click", () => hideNotice());

    this.mountNavIcons();
    this.wireNavRail();
    this.mountRailLogo();
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

  /** Per-CLI Claude/Copilot/Codex checkboxes drive wiring for BOTH the
   *  Studio Pro MCP (mendix-studio-pro) and Concord MCP (concord-mcp)
   *  entries — the same mcpClients list is consumed by both apply paths
   *  in C#. The UI homes them inside the Concord MCP section because that
   *  section is always visible (the Studio Pro MCP section is hidden on
   *  versions < 11.10). Enable-state logic:
   *    • Both MCP toggles off  → per-CLI disabled (no wiring would happen).
   *    • Either MCP toggle on  → per-CLI enabled (at least one server uses them).
   *  Toggling the Studio Pro MCP master no longer clears the per-CLI
   *  selection — that would unwire Concord MCP, which is the regression
   *  the v5.0.0-alpha.3 restructure addresses. */
  private onMcpEnabledChange() {
    this.updateCliCheckboxesEnabled();
  }

  private onActionsEnabledChange() {
    this.updateCliCheckboxesEnabled();
  }

  private updateCliCheckboxesEnabled() {
    const anyMcpEnabled = this.chkMcp.checked || this.chkActions.checked;
    this.chkMcpClaude.disabled = !anyMcpEnabled;
    this.chkMcpCopilot.disabled = !anyMcpEnabled;
    this.chkMcpCodex.disabled = !anyMcpEnabled;
  }

  /** When the master Skills toggle flips, sync the per-CLI checkboxes:
   *  - turning OFF unchecks them and disables them
   *  - turning ON re-enables them (leaves their last values alone) */
  private onSkillsEnabledChange() {
    const enabled = this.chkSkillsEnabled.checked;
    if (!enabled) {
      this.chkSkillsClaude.checked = false;
      this.chkSkillsCopilot.checked = false;
      this.chkSkillsCodex.checked = false;
    }
    this.chkSkillsClaude.disabled = !enabled;
    this.chkSkillsCopilot.disabled = !enabled;
    this.chkSkillsCodex.disabled = !enabled;
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
    // Studio Pro MCP (mendix-studio-pro) was introduced in Studio Pro 11.10.
    // On 10.x or 11.6–11.9 the C# probe returns available=false, in which
    // case we hide the entire section AND clamp `mcpEnabled` to off. We do
    // NOT clear the per-CLI checkboxes here — the SAME mcpClients list also
    // drives Concord MCP (concord-mcp) wiring in ApplyActionsMcpConfig, so
    // zeroing it would unwire concord-mcp from the user's CLIs on save.
    const spMcpAvailable = d.studioProMcp?.available === true;
    this.studioProMcpAvailable = spMcpAvailable;
    this.applyStudioProMcpAvailabilityGate(spMcpAvailable);
    this.chkMcp.checked = spMcpAvailable && !!d.mcpEnabled;
    this.renderMcpPortReadout(d.studioProMcp);
    const clients = new Set((d.mcpClients ?? []).map((c) => c.toLowerCase()));
    this.chkMcpClaude.checked = clients.has("claude");
    this.chkMcpCopilot.checked = clients.has("copilot");
    this.chkMcpCodex.checked = clients.has("codex");
    // Apply enabled/disabled to children based on master state.
    this.onMcpEnabledChange();

    // Concord MCP fields
    this.chkActions.checked = d.mcpServerEnabled;
    this.chkSpActions.checked = d.studioProActionsEnabled;
    // Maia controls are only meaningful on Studio Pro 11.10+ (the version
    // that ships the Maia panel). On older versions the rows are hidden
    // and the checkboxes forced off — the C# probe gates this via
    // maiaAvailable on the payload.
    this.maiaAvailable = d.maiaAvailable === true;
    this.chkMaia.checked = this.maiaAvailable && d.maiaIntegrationEnabled;
    this.chkMaiaDiagnostic.checked = this.maiaAvailable && d.maiaDiagnosticLogging;
    this.renderActionsPortReadout(d.mcpServerEnabled, d.liveActionServerPort);
    this.applyMaiaAvailabilityGate(this.maiaAvailable, d.platform);
    this.inpRefreshHotkey.value = d.refreshFromDiskHotkey;
    this.onMcpEnabledChange(); // also flips actions enabled state

    // Skills
    this.chkSkillsEnabled.checked = !!d.skillsEnabled;
    const skillClients = new Set(
      (d.skillClients ?? []).map((c) => c.toLowerCase()),
    );
    this.chkSkillsClaude.checked = skillClients.has("claude");
    this.chkSkillsCopilot.checked = skillClients.has("copilot");
    this.chkSkillsCodex.checked = skillClients.has("codex");
    this.onSkillsEnabledChange();
    this.renderBundledSkillsList(d.bundledSkills ?? []);

    // About section
    this.populateAbout(d.about);

    // (Port-mismatch banner + chrome status pill retired — the readouts
    // inside Settings → Studio Pro MCP and Settings → Action bridge are
    // the canonical state surfaces now.)
  }

  /** Read-only port readout under the Concord MCP enable checkbox.
   *  Shows the live bound port (or a friendly "off" / "starting…" state). */
  private renderActionsPortReadout(
    enabled: boolean,
    livePort: number | null,
  ): void {
    if (!this.actionsPortReadout) return;
    if (!enabled) {
      this.actionsPortReadout.classList.remove("warn");
      this.actionsPortReadout.innerHTML = `Concord MCP is off. Enable to let your CLIs drive Studio Pro directly.`;
      return;
    }
    if (livePort == null) {
      this.actionsPortReadout.classList.add("warn");
      this.actionsPortReadout.innerHTML = `Concord MCP starting…`;
      return;
    }
    this.actionsPortReadout.classList.remove("warn");
    this.actionsPortReadout.innerHTML = `Connected on <code>localhost:${livePort}</code>.`;
  }

  /** Show or hide the entire "Studio Pro MCP" section based on whether the
   *  running Studio Pro version exposes the mendix-studio-pro MCP server
   *  (11.10+). When unavailable, the nav-item disappears from the rail AND
   *  the section panel is removed from the DOM flow — the user has no way
   *  to navigate to or activate it. The "Concord MCP" / "Skills" / "About"
   *  sections are unaffected. */
  private applyStudioProMcpAvailabilityGate(available: boolean): void {
    const navItem = document.querySelector<HTMLElement>(
      '.nav-item[data-section="mcp"]',
    );
    const section = document.querySelector<HTMLElement>(
      '.settings-section[data-section="mcp"]',
    );
    const show = available;
    if (navItem) navItem.style.display = show ? "" : "none";
    if (section) section.style.display = show ? "" : "none";
    // If the user happens to be focused on the now-hidden section (e.g.
    // they had it open before a project reload onto an older Studio Pro),
    // switch them to General so the modal doesn't look blank.
    if (!show && section?.classList.contains("active")) {
      this.activateSection("general");
    }
  }

  /** Hide or show the Maia checkbox + diagnostic + platform-note rows based
   *  on whether the running Studio Pro version exposes the Maia AI panel
   *  (11.10+). When unavailable, the entire row group disappears so older
   *  Studio Pro users don't see dead toggles. When available, falls through
   *  to the Windows-only platform gate. */
  private applyMaiaAvailabilityGate(available: boolean, platform: string): void {
    const rows = [
      this.chkMaia.closest(".checkbox-row") as HTMLElement | null,
      this.chkMaiaDiagnostic.closest(".checkbox-row") as HTMLElement | null,
      this.maiaPlatformNote as HTMLElement | null,
    ];
    for (const row of rows) {
      if (row) row.style.display = available ? "" : "none";
    }
    if (available) {
      this.applyMaiaPlatformGate(platform);
    }
  }

  private applyMaiaPlatformGate(platform: string): void {
    const isWindows = platform === "windows";
    this.chkMaia.disabled = !isWindows;
    // v4.2.0: diagnostic logging is meaningless without a working bridge,
    // so it follows the same Windows-only gate as Maia integration itself.
    this.chkMaiaDiagnostic.disabled = !isWindows;
    if (this.maiaPlatformNote) {
      this.maiaPlatformNote.classList.remove("warn");
      this.maiaPlatformNote.innerHTML = isWindows
        ? `Maia integration uses Studio Pro's WebView2 debug port. Maia panel must be visible at call time.`
        : `Maia integration is <strong>Windows-only</strong> in this Concord release.`;
    }
  }

  /** Read-only port readout under the MCP enable checkbox. Surfaces what
   *  Studio Pro is actually serving on (or warns when it can't tell). */
  private renderMcpPortReadout(sp: StudioProMcpInfo | null): void {
    if (!this.mcpPortReadout) return;
    if (sp?.enabled === true && sp.port != null) {
      this.mcpPortReadout.classList.remove("warn");
      this.mcpPortReadout.innerHTML = `Connected on <code>localhost:${sp.port}</code>.`;
      return;
    }
    if (sp?.enabled === false) {
      this.mcpPortReadout.classList.add("warn");
      this.mcpPortReadout.innerHTML = `Studio Pro MCP is off. Enable in <em>Edit → Preferences → Maia → MCP Server</em>, then reopen Concord.`;
      return;
    }
    // Probe couldn't tell — file unreadable, schema mismatch, etc.
    this.mcpPortReadout.classList.add("warn");
    this.mcpPortReadout.innerHTML = `Studio Pro MCP not detected. Check <em>Edit → Preferences → Maia → MCP Server</em> is enabled, then reopen Concord.`;
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

  private renderBundledSkillsList(skills: BundledSkill[]) {
    this.bundledSkillsList.replaceChildren();
    for (const s of skills) {
      const li = document.createElement("li");

      const nameEl = document.createElement("span");
      nameEl.className = "skill-name";
      nameEl.textContent = s.name;
      li.appendChild(nameEl);

      if (s.description) {
        const descEl = document.createElement("span");
        descEl.className = "skill-desc";
        descEl.textContent = s.description;
        li.appendChild(descEl);
      }
      this.bundledSkillsList.appendChild(li);
    }
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

    // Defensive clamp on mcpEnabled (Studio Pro MCP toggle): when Studio Pro
    // doesn't expose mendix-studio-pro, send Off regardless of form state.
    // Per-CLI mcpClients is NOT clamped — the same list also drives Concord
    // MCP (concord-mcp) wiring in ApplyActionsMcpConfig, so zeroing it would
    // unwire concord-mcp from the user's CLIs. The C# apply helper already
    // skips the mendix-studio-pro upsert when McpEnabled is false.
    const mcpEnabledFinal = this.studioProMcpAvailable && this.chkMcp.checked;
    const mcpClients: string[] = [];
    if (this.chkMcpClaude.checked) mcpClients.push("claude");
    if (this.chkMcpCopilot.checked) mcpClients.push("copilot");
    if (this.chkMcpCodex.checked) mcpClients.push("codex");

    const skillClients: string[] = [];
    if (this.chkSkillsClaude.checked) skillClients.push("claude");
    if (this.chkSkillsCopilot.checked) skillClients.push("copilot");
    if (this.chkSkillsCodex.checked) skillClients.push("codex");

    this.bridge.send("saveSettings", {
      shellPath,
      args: args ? args.split(/\s+/) : [],
      ringBufferKB: parseInt(this.inpRing.value, 10) || 4096,
      xtermScrollbackLines: parseInt(this.inpScroll.value, 10) || 10000,
      theme,
      mcpEnabled: mcpEnabledFinal,
      mcpClients,
      mcpServerEnabled: this.chkActions.checked,
      studioProActionsEnabled: this.chkSpActions.checked,
      // Defensive clamp: when Studio Pro has no Maia panel (10.x, 11.6–11.9),
      // send both Maia flags off regardless of form state. Mirrors the
      // McpEnabled clamp for the Studio Pro MCP feature.
      maiaIntegrationEnabled: this.maiaAvailable && this.chkMaia.checked,
      maiaDiagnosticLogging: this.maiaAvailable && this.chkMaiaDiagnostic.checked,
      refreshFromDiskHotkey: this.inpRefreshHotkey.value,
      restoreTabsOnReopen: this.chkRestoreTabs.checked,
      skillsEnabled: this.chkSkillsEnabled.checked,
      skillClients,
    });

    this.onThemeChanged(theme);
    this.onScrollbackChanged(parseInt(this.inpScroll.value, 10) || 10000);
    this.close();
  }
}
