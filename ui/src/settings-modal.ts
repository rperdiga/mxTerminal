import { Bridge } from "./bridge.js";

export class SettingsModal {
  private modal = document.getElementById("settings-modal") as HTMLDivElement;
  private inpShell = document.getElementById("set-shell") as HTMLInputElement;
  private inpArgs = document.getElementById("set-args") as HTMLInputElement;
  private inpRing = document.getElementById("set-ring") as HTMLInputElement;
  private inpScroll = document.getElementById("set-scroll") as HTMLInputElement;

  constructor(private bridge: Bridge, private onScrollbackChanged: (lines: number) => void) {
    document.getElementById("btn-settings")!.addEventListener("click", () => this.open());
    document.getElementById("set-cancel")!.addEventListener("click", () => this.close());
    document.getElementById("set-save")!.addEventListener("click", () => this.save());

    bridge.on("settings", (d: { shellPath: string; args: string[]; ringBufferKB: number; xtermScrollbackLines: number }) => {
      this.inpShell.value = d.shellPath;
      this.inpArgs.value = d.args.join(" ");
      this.inpRing.value = String(d.ringBufferKB);
      this.inpScroll.value = String(d.xtermScrollbackLines);
      this.onScrollbackChanged(d.xtermScrollbackLines);
    });
  }

  open() {
    this.bridge.send("openSettings");
    this.modal.classList.add("visible");
  }

  close() { this.modal.classList.remove("visible"); }

  private save() {
    const args = this.inpArgs.value.trim();
    this.bridge.send("saveSettings", {
      shellPath: this.inpShell.value.trim() || "powershell.exe",
      args: args ? args.split(/\s+/) : [],
      ringBufferKB: parseInt(this.inpRing.value, 10) || 4096,
      xtermScrollbackLines: parseInt(this.inpScroll.value, 10) || 10000,
    });
    this.onScrollbackChanged(parseInt(this.inpScroll.value, 10) || 10000);
    this.close();
  }
}
