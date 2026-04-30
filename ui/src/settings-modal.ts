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

    bridge.on("settings", (d: SettingsPayload) => this.populate(d));
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

    this.bridge.send("saveSettings", {
      shellPath,
      args: args ? args.split(/\s+/) : [],
      ringBufferKB: parseInt(this.inpRing.value, 10) || 4096,
      xtermScrollbackLines: parseInt(this.inpScroll.value, 10) || 10000,
      theme,
    });

    applyBodyTheme(theme);
    this.onThemeChanged(theme);
    this.onScrollbackChanged(parseInt(this.inpScroll.value, 10) || 10000);
    this.close();
  }
}
