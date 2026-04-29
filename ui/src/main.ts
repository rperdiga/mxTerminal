import { Bridge } from "./bridge.js";
import { TabManager } from "./tab-manager.js";
import { SettingsModal } from "./settings-modal.js";

function boot() {
  const bridge = new Bridge();
  const tabsContainer = document.getElementById("tabs") as HTMLDivElement;
  const terminalsContainer = document.getElementById("terminals") as HTMLDivElement;
  const tabMgr = new TabManager(bridge, tabsContainer, terminalsContainer);
  const settings = new SettingsModal(bridge, lines => tabMgr.setScrollbackLines(lines));

  document.getElementById("btn-new")!.addEventListener("click", () => tabMgr.newTab());

  // Tell C# we're ready, then ask what tabs already exist
  bridge.send("ready");
  bridge.send("listTabs");

  // First-time use: if no tabs after a short delay, open one
  setTimeout(() => {
    if ((document.getElementById("tabs") as HTMLDivElement).childElementCount === 0) {
      tabMgr.newTab();
    }
  }, 200);

  // Suppress unused warning
  void settings;
}

if (document.readyState === "loading")
  document.addEventListener("DOMContentLoaded", boot);
else
  boot();
