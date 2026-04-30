import { Bridge } from "./bridge.js";
import { TabManager } from "./tab-manager.js";
import { SettingsModal } from "./settings-modal.js";

function boot() {
  const bridge = new Bridge();
  const tabsContainer = document.getElementById("tabs") as HTMLDivElement;
  const terminalsContainer = document.getElementById("terminals") as HTMLDivElement;
  const tabMgr = new TabManager(bridge, tabsContainer, terminalsContainer);
  const settings = new SettingsModal(
    bridge,
    lines => tabMgr.setScrollbackLines(lines),
    theme => tabMgr.setTheme(theme),
  );

  document.getElementById("btn-new")!.addEventListener("click", () => tabMgr.newTab());

  // F12 opens Studio Pro's WebView DevTools (xterm captures right-click,
  // so that path doesn't work). Listen at window-level capture phase so
  // xterm's keyboard handlers don't swallow it.
  window.addEventListener("keydown", (e) => {
    if (e.key === "F12") {
      e.preventDefault();
      bridge.send("showDevTools");
    }
  }, true);

  // Fetch settings first so the theme + scrollback are applied before any
  // xterm instance is constructed (the C# `settings` reply triggers
  // SettingsModal.populate which calls back into tabMgr.setTheme/setScrollbackLines).
  bridge.send("openSettings");
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
