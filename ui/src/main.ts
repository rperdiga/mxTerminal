import { Bridge } from "./bridge.js";
import { TabManager } from "./tab-manager.js";
import { SettingsModal } from "./settings-modal.js";
import { mountIcon } from "./icons.js";
import { onSystemThemeChange } from "./theme.js";

function boot() {
  const bridge = new Bridge();
  const tabsContainer = document.getElementById("tabs") as HTMLDivElement;
  const terminalsContainer = document.getElementById(
    "terminals",
  ) as HTMLDivElement;
  const tabMgr = new TabManager(bridge, tabsContainer, terminalsContainer);
  const settings = new SettingsModal(
    bridge,
    (lines) => tabMgr.setScrollbackLines(lines),
    (theme) => tabMgr.setTheme(theme),
  );

  // Mount Lucide SVG icons into the chrome buttons (replaces unicode + ⚙).
  const btnNew = document.getElementById("btn-new")!;
  const btnSettings = document.getElementById("btn-settings")!;
  const bannerClose = document.getElementById("banner-close")!;
  mountIcon(btnNew, "plus");
  mountIcon(btnSettings, "settings");
  mountIcon(bannerClose, "x");

  // Click + Enter/Space activation for the chrome buttons (now that they
  // are role=button divs, they need explicit keyboard handling).
  const activate = (el: HTMLElement, fn: () => void) => {
    el.addEventListener("click", fn);
    el.addEventListener("keydown", (e: KeyboardEvent) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        fn();
      }
    });
  };
  activate(btnNew, () => tabMgr.newTab());

  // Auto-follow Studio Pro's theme. matchMedia fires when the WebView host
  // flips light/dark; we only re-apply if the user's setting is "auto",
  // otherwise their explicit pick wins. setTheme(theme) is a no-op visual
  // change when theme is already dark or light.
  onSystemThemeChange(() => {
    if (tabMgr.getTheme() === "auto") tabMgr.setTheme("auto");
  });

  // F12 opens Studio Pro's WebView DevTools (xterm captures right-click,
  // so that path doesn't work). Listen at window-level capture phase so
  // xterm's keyboard handlers don't swallow it.
  window.addEventListener(
    "keydown",
    (e) => {
      if (e.key === "F12") {
        e.preventDefault();
        bridge.send("showDevTools");
      }
    },
    true,
  );

  // Fetch settings first so the theme + scrollback are applied before any
  // xterm instance is constructed (the C# `settings` reply triggers
  // SettingsModal.populate which calls back into tabMgr.setTheme/setScrollbackLines).
  bridge.send("openSettings");
  bridge.send("listTabs");

  // First-time use: if no tabs after a short delay, open one
  setTimeout(() => {
    if (
      (document.getElementById("tabs") as HTMLDivElement).childElementCount ===
      0
    ) {
      tabMgr.newTab();
    }
  }, 200);

  // Suppress unused warning
  void settings;
}

if (document.readyState === "loading")
  document.addEventListener("DOMContentLoaded", boot);
else boot();
