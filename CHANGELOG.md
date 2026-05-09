# Changelog

## 4.1.3 — 2026-05-09

### Added

- **Mac variant of the `mendix-page-gen` skill pack.** The Windows version of this skill instructs the CLI agent to delegate page writes to Maia (via Concord MCP's `maia__ask`) because Studio Pro's MCP doesn't expose `pg_read_page` / `pg_write_page` tools. On macOS, Maia integration isn't available (WKWebView can't be inspected externally without host opt-in — see `docs/MAIA_MAC_FEASIBILITY.md`), so a new Mac-specific variant ships at `skills-mac/mendix-page-gen/SKILL.md`. Same widget catalog and rules; the head section is rewritten to print a copy-paste hand-off prompt for the user ("Open Maia in Studio Pro, paste this, send, reply `done`") and then stop and wait. After the user confirms, the CLI verifies with `ped_check_errors` directly. The other 6 skill packs (`mendix-microflow-*`, `mendix-view-entities`, `mendix-workflow-*`) don't reference Maia and remain platform-identical.
- **`SkillInstaller` overlay support.** Optional `overlaySkillsRoot` constructor parameter. After copying the primary bundled skill folders into the target subdir, the installer copies the overlay root on top — same-named files inside same-named skill folders win. Lets us swap one skill for a platform-specific variant without forking all 7.
- **`SettingsApplyHelper` auto-derives the Mac overlay.** When `OperatingSystem.IsMacOS()` is true and `<bundledSkillsRoot>/../skills-mac` exists, the helper passes it as the overlay to `SkillInstaller`. The diff log line now includes `overlay-root=...` so you can confirm at runtime by tailing `terminal.log`.
- **`docs/MAIA_MAC_FEASIBILITY.md`.** Research write-up explaining why a CDP-style Maia transport on Mac isn't feasible: WKWebView's `isInspectable` requires host opt-in (Mendix would have to flip it on the Maia WebView in Studio Pro itself), `_developerExtrasEnabled` is similarly host-side, and the Mendix Extensions API 11.6.2 has no Maia-related surface. Documents three forward paths: an AX/osascript Tier-3 transport (~1-2 day prototype, brittle), a feature request to Mendix for an `IMaiaService` extension API, or a feature request for a Mac `--remote-debugging-port`-equivalent debug flag. Also records the rejected SIP-bypass route (unshippable). Linked from the README's macOS callout.
- **Tests:** `InstallAll_OverlayReplacesPrimarySkill` and `InstallAll_OverlayMissingDoesNotThrow` in `SkillInstallerTests`.

### Changed

- **README's macOS callout** rewritten: `Maia integration is Windows-only in this release` → `Maia integration is Windows-only` + an explicit pointer to the feasibility doc + a new section under "Bundled skill packs" explaining the Mac variant of `mendix-page-gen`.

### Notes

- **No Windows-side change.** The Mac scoping is gated on `OperatingSystem.IsMacOS()`. The `skills-mac/` overlay directory is shipped with every build but only consumed on macOS. Windows users see no functional change.
- **Existing Mac customers materialize the Mac variant on upgrade.** `IsUpgradeApplyNeeded` fires on the strict-older `lastAppliedVersion` stamp (`4.1.2 < 4.1.3`), the apply chain re-runs `SkillInstaller.InstallAll` for ticked CLIs, the Mac overlay copies on top of the primary, and `<project>/.claude/skills/mendix-page-gen/SKILL.md` ends up with the Mac hand-off copy. No manual Save needed. Banner: `Updated to 4.1.3. Rewired: Skill packs installed: Claude Code, Copilot CLI. Open Settings to adjust.`
- **Maia gating itself was already in place in 4.1.2.** Both action-server construction sites (`TerminalPaneViewModel.cs` and `TerminalPaneExtension.cs`) already computed `maiaEnabled = OperatingSystem.IsWindows() && setting`, so the `maia__*` tool family was already excluded from the Concord MCP advertise list on Mac. 4.1.3 just documents the behavior + adds the matching Mac-side skill flow.

## 4.1.2 — 2026-05-08

### Fixed

- **Port-leak in `terminal-settings.json`.** The settings modal's outgoing payload was sending the LIVE bound port of the Concord MCP server to the JS side (so the readout could display "listening on `localhost:8099`" when 7783 was busy). The incoming Save handler then persisted that live port to disk as if it were configuration intent. Next launch the runtime ignored it (correct — the server always probes a free port) but the saved value stuck in the file forever, displayed as a phantom "configured port" the user never chose. **Fix:** `McpServerPort`, `McpPort`, and the legacy `ActionsServerPort` keys are removed from the settings schema entirely. Old keys in existing files deserialize as ignored fields and disappear on the next save. The Concord MCP listening port is now exposed only through a read-only display field (`liveActionServerPort`) that is never echoed back through Save.
- **Settings modal title.** "Concord Terminal Settings" → "Concord Settings" (the modal already lives inside the terminal pane).
- **Save vs. Cancel button affordance** in dark mode. Both rendered as the same gray surface, making the primary action invisible. Save now carries an accent-colored border without violating Studio Pro's no-filled-primary-button convention. Cancel relabeled to "Close" — settings UX never "cancels" pending intent the user already saw applied.

### Changed

- **Banner copy** rewritten to read like product, not like log lines:
  - First-run: `Concord wired up for first-time use: ...` → `Concord ready. Wired: ...`
  - Upgrade with changes: `Concord upgraded (X → Y). Refreshed wiring: ... Open Settings to customize.` → `Updated to Y. Rewired: ... Open Settings to adjust.`
  - Upgrade no changes: `Concord upgraded to Y. No wiring changes needed.` → `Updated to Y. No changes.`
  - SP-MCP advisory: `Studio Pro's MCP server appears disabled. Enable it in Edit → Preferences → Maia → MCP Server, then reopen this pane to make the wired CLI configs functional.` → `Studio Pro MCP is off. Enable it in Edit → Preferences → Maia → MCP Server, then reopen Concord.`
  - Maia advisory: `Maia tools require the Maia panel to be visible. Keep it open while Claude Code or Copilot CLI drives Maia.` → `Keep the Maia panel open while Maia tools are in use.`
- **Save-result strings** cleaned up: `MCP servers updated for ...` → `MCP wired for ...`; `skill packs installed for X skills` → `Skill packs installed: X` (no more triple "skills").
- **Concord MCP port readout** reduced from a 5-line wall of prose to a single status line: `Connected on localhost:7783.` (or `Concord MCP starting…` / `Concord MCP is off.`).
- **Modal open animation** trimmed from 180ms to 140ms (modal-in-modal context, the slower curve read sluggish).
- **Footer credit** reworded: `A Siemens CoE extension for Studio Pro.` → `Built by the Siemens CoE Team.`
- **README current-version** corrected (was stale at 4.0.0; this release is 4.1.2).

### Added

- **Atomic file writes** (`File.Replace`) in `McpJsonConfigurator`, `McpTomlConfigurator`, and `TerminalSettings.Save`. Previous delete-then-move pattern had a brief window where AV scanners or concurrent readers saw the file gone; if the move then failed, the user was left with no file at all. Journaled NTFS rename closes the window.
- **Corrupt-file backup** in `TerminalSettings.Load`. A malformed `terminal-settings.json` no longer silently defaults — it's renamed to `terminal-settings.json.broken-{timestamp}.bak` so the user can recover their custom shell/theme/etc., then defaults take over.
- **JSON serializer skips nulls** when writing settings. Removes residual `"actionsServerEnabled": null` (and any future legacy back-compat field that goes null) from the saved file.

### Notes

- **Existing customers automatically benefit.** The upgrade-apply path (introduced in 4.1.1) fires on first open after the 4.1.2 install, runs the apply chain, writes a fresh `terminal-settings.json` without the stale port keys, and stamps `lastAppliedVersion: 4.1.2`. No manual cleanup required.

## 4.1.1 — 2026-05-08

### Added

- **Upgrade auto-apply.** When Concord opens against a project that has a `terminal-settings.json` file from an older Concord version (compared via `lastAppliedVersion` semver stamp), the wiring keys (`McpEnabled`, `McpClients`, `McpServerEnabled`, sub-toggles, `SkillsEnabled`, `SkillClients`) are re-defaulted to current `Defaults()` values and re-applied to disk — so customers who upgrade from 1.x or 4.0.0 get the new MCP wiring + skill packs materialized without needing to open Settings and Save manually. Runtime preferences (shell, theme, ring buffer, scrollback, restore-tabs, refresh hotkey) are preserved verbatim. Banner: `Updated to {ver}. Rewired: ... Open Settings to adjust.`
- **Cross-machine safety.** `IsUpgradeApplyNeeded` only fires on a strict-older stamp (`prev < curr` in System.Version semantics). A colleague pulling a project from a machine running a more recent Concord version sees no apply (their wiring wins; no downgrade). Once stamped current, subsequent opens at the same version are no-ops.

## 4.1.0 — 2026-05-08

### Added

- **Default-on settings + first-run auto-apply.** `TerminalSettings.Defaults()` now returns all toggles on (Claude Code + Copilot CLI for both MCP families and skills). When Concord opens against a project that has no `resources/terminal-settings.json` yet, it writes `.mcp.json`, installs bundled skill packs into `.claude/skills/` and `.github/skills/`, and persists the settings file in one go — no modal clicks required. The Concord MCP bridge starts automatically through the existing `TryAutoStartActionServer` path now that the trigger condition is on by default.
- **First-run advisory banners.** On a fresh install, three banners appear via the existing `mcpResult` channel: a "Concord wired up" summary, a Studio Pro MCP-disabled advisory (if the SQLite probe shows it disabled in Preferences), and a Maia-pane-open advisory on Windows.

### Notes

- **Codex stays opt-in.** Auto-enabling Codex would write to user-global `~/.codex/config.toml` and `~/.codex/skills/` (a side effect outside the project tree). The customer can flip Codex on per-section in Settings.
- **Existing customers are not retroactively flipped.** The auto-apply only runs when no `resources/terminal-settings.json` file exists. Anyone with a saved settings file from 4.0.0 keeps their explicit choices.
- **Edge case (very old settings files).** If a 1.x or 2.x settings file is loaded that's missing `skillsEnabled`/`skillClients` keys entirely, `Load()` migration applies the new defaults in memory (skills appear enabled in the modal). The disk state isn't auto-applied — the next Save makes it consistent.

### Refactor

- The apply-on-save chain (MCP json/toml writers + skill installer) was extracted from `TerminalPaneViewModel` into a static `SettingsApplyHelper` so the extension's first-run path can call the same code without taking a ViewModel dependency. The orchestration layer now has unit-test coverage that didn't exist before.

## 4.0.0 — 2026-05-08

> **About the version jump (1.3.0 → 4.0.0).** The 4.0 major reflects
> the MCP wire-identity rename shipped in 1.3.0 (`mendix-studio-pro-actions`
> → `concord-mcp`) — a breaking change for any client config that
> referenced the old name — combined with the bundled-skills ship in
> 4.0.0. The 2.x and 3.x series are intentionally skipped to align the
> major version with the Concord product brand (a 4.0 launches feels
> right for what shipped together on 2026-05-08; renumbering historical
> commits would be a worse trade-off).

### Added

- **Bundled Mendix skill packs.** The Skills section of the settings modal is now a working installer: enable per-CLI to write the Concord-bundled skills into `<project>/.claude/skills/`, `<project>/.github/skills/`, and/or `<project>/.codex/skills/`. Disable a CLI to remove only the Concord-bundled folders — user-authored siblings under the same directory are left intact. Each Save refreshes the bundled content so a Concord upgrade ships new or updated skills automatically.
- **7 Mendix skills** ship in this release: `mendix-microflow-common`, `mendix-microflow-syntax`, `mendix-microflow-update`, `mendix-page-gen`, `mendix-view-entities`, `mendix-workflow-common`, `mendix-workflow-update`.

### Notes

- Skills are installed project-local only in this release (no `~/.claude/skills/` writes).
- If you have hand-edited a Concord-bundled skill folder, your edits will be overwritten on the next Save. Add custom skills as siblings (e.g. `<project>/.claude/skills/my-thing/`) to keep them safe across upgrades.

## 1.3.0 — 2026-05-08

### Breaking
- MCP server wire identity renamed from `mendix-studio-pro-actions` to `concord-mcp`. Update any MCP client config (Claude Code `.mcp.json`, Codex `~/.codex/config.toml`, Copilot CLI) that references the old name.

### Added
- **Maia integration** as a first-class tool family inside Concord MCP, embedded in C# (no Python, no subprocess). Tools: `maia__send`, `maia__status`, `maia__wait`, `maia__ask`, `maia__reset`, `maia__force_tier`. Two-tier transport: injected JS agent (Tier 1) + DOM-scrape fallback (Tier 2). Windows only.
- Settings sidebar item renamed: `Action bridge` → `Concord MCP`. Two sub-toggles: `Studio Pro UI actions` and `Maia integration`. Maia disabled-with-tooltip on macOS.
- New settings keys: `mcpServerEnabled`, `mcpServerPort`, `studioProActionsEnabled`, `maiaIntegrationEnabled`. Old keys (`actionsServerEnabled`, `actionsServerPort`) read for one minor-version migration.

### Note
- Maia integration is internal CoE tooling. The CDP-driven approach (Studio Pro's WebView2 `--remote-debugging-port`) is not Mendix-blessed and may break if Mendix changes that surface. The transport interface is the swap-out seam for future Mendix-native MCP-server-as-tool support.

## 1.2.2 — 2026-05-07

### Action bridge keystrokes now reach Studio Pro on Mac

In 1.2.1, `osascript` was successfully sending F5 / Shift+F5 to Studio
Pro — but the keystrokes landed in the xterm inside the Concord pane,
not in Studio Pro's main accelerator handler. Visible in the log as a
`JS: onData len=5` entry firing within milliseconds of every
`[actions] sent F5` entry: F5's VT escape sequence was being absorbed
by xterm.js because the WKWebView held first-responder status.

Fixed in `src/StudioProUiAutomation.cs` by clearing the WebView's
first-responder grip via AppKit P/Invoke before each Mac keystroke
send:

```objc
[[[NSApplication sharedApplication] keyWindow] makeFirstResponder:nil]
```

Marshalled via Eto.Forms's `Application.Instance.Invoke` so the AppKit
call lands on the main thread — the action HTTP server otherwise runs
on the thread pool. Falls back to `mainWindow` if `keyWindow` is nil,
and falls through silently if AppKit can't be reached (best-effort:
worst case is the previous broken behavior, not a crash).

After 1.2.2, the keystroke reaches Studio Pro's main UI and triggers
Run / Stop / Refresh as expected. Side-effect: the user needs to click
back into the Concord pane after the action fires if they want to type
more — first responder isn't restored.

### Files touched

- `src/StudioProUiAutomation.cs` — `ClearWebViewFirstResponder` helper
  + private `MacAppKit` static class with `objc_getClass` /
  `sel_registerName` / `objc_msgSend` P/Invoke. Called from `SendMac`
  before invoking osascript.

## 1.2.1 — 2026-05-07

### Action bridge now works on macOS

In 1.2.0 the four hotkey-based action tools (`run_app`, `stop_app`,
`refresh_project`, `save_all`) silently no-op'd on Mac — they used Win32
`PostMessage`, which has no equivalent on Mac. This release adds a real
Mac backend.

**Implementation (`src/StudioProUiAutomation.cs`).** New `SendMac` path
invokes `/usr/bin/osascript` with a one-shot AppleScript that:

1. Looks up our own process via Unix PID (`Environment.ProcessId`) — so
   the lookup is stable regardless of the `.app`'s display name
   ("Mendix Studio Pro 11.10.0 Beta.app" today, something else
   tomorrow).
2. Brings Studio Pro to the foreground via `set frontmost of sp to
   true`.
3. Sends `key code N [using {modifiers}]` to deliver the keystroke.

Key codes use Apple's HIToolbox values from `Events.h`. Modifier
mapping: Ctrl → control down, Shift → shift down, Alt → option down.

**Permission-aware error reporting.** When osascript fails with
AppleEvent `-1719` ("not allowed assistive access"), the bridge surfaces
a specific user-actionable message instead of the generic "main window
unavailable" string from 1.2.0:

> "macOS Accessibility permission not granted to Studio Pro. Open System
> Settings → Privacy & Security → Accessibility, enable Studio Pro (add
> it with the + button if it isn't listed), then restart Studio Pro and
> retry."

This message rides through `IStudioProUiAutomation.LastFailureReason`
into `ActionResult.Error`, so Claude / Codex see it directly and can
guide the user. New `RunApp_TriggerFails_PropagatesUiFailureReason` test
covers the propagation.

### Tests + tooling

- New `IStudioProUiAutomation.LastFailureReason` property (test mocks
  updated)
- 96 xunit tests passing on Mac (was 95 in 1.2.0)

### Files touched

- `src/IStudioProUiAutomation.cs` — `LastFailureReason` property
- `src/StudioProUiAutomation.cs` — Win/Mac dispatch, `SendMac`
  via osascript, AppleEvent error mapping, Win VK → macOS HIToolbox
  key code table
- `src/StudioProActions.cs` — surface `ui.LastFailureReason` in failure
  ActionResults
- `tests/StudioProActionServerTests.cs`, `tests/StudioProActionsTests.cs`
  — mock updates + propagation test

## 1.2.0 — 2026-05-07

### macOS support

Concord now runs on Studio Pro for Mac in addition to Windows. The C#
extension, the WebView UI, and the test suite all branch on
`OperatingSystem.IsMacOS()` / `IsWindows()` so a single build of
`Concord.dll` works on either host.

**POSIX PTY backend (`src/UnixPtySession.cs`).** Mirrors the
`IPtySession` surface of `ConPtySession` but built directly on
`libSystem.dylib` — `openpty(3)` to allocate the pty pair,
`posix_spawn_file_actions_*` + `posix_spawnp` to wire the slave fd to
stdin/stdout/stderr, `posix_spawn_file_actions_addchdir_np` (macOS
10.15+) to chdir before exec so shells start in the project root rather
than the Studio Pro `.app` bundle. EOF on the master fd is signaled by
`EIO` rather than a 0-byte read on Darwin — caught and surfaced as EOF
to preserve the cross-platform `IPtySession` contract. Dispose order is
SIGHUP → bounded waitpid → SIGKILL → close, with a watchdogged
`close()` call (a stuck close on a pending master read can otherwise
freeze the UI thread for minutes — verified during bring-up).

**WKWebView bridge (`ui/src/bridge.ts`).** Detects WebView family at
runtime and dispatches accordingly: `window.chrome.webview` for
WebView2 on Windows, `window.webkit.messageHandlers.studioPro` +
`window.WKPostMessage` for WKWebView on Mac. WKWebView requires JSON
string payloads; WebView2 accepts objects directly.

**WKWebView focus + keyboard fixes (`ui/src/xterm-tab.ts`).**
WKWebView refuses programmatic focus on the off-screen helper
`<textarea>` xterm.js relies on for input; we reposition the helper
on-screen with `opacity: 0` and walk the focus chain on mousedown.
A document-level keydown→VT100-bytes fallback fires when the textarea
doesn't receive first-responder, mapping arrows / function keys / Enter /
Backspace / Ctrl-letter combos to the byte sequences a TUI expects.

**Settings.sqlite probe — Mac path (`src/StudioProThemeProbe.cs`).**
Studio Pro on Mac persists its preferences at
`~/Library/Application Support/Mendix/Settings.sqlite`, not the XDG
`~/.local/share` path that .NET's `LocalApplicationData` resolves to on
Darwin. The probe branches explicitly. New
`tests/StudioProThemeProbeTests.cs` covers both Windows and Mac path
resolution.

**Shell handling.** `ShellDetector` returns `$SHELL` (typically zsh on
modern macOS), `/bin/zsh`, `/bin/bash`, `/bin/sh`, plus `pwsh` if on
PATH. `TerminalSettings.MigrateShellPathForPlatform` rewrites obviously
incompatible saved values (`cmd.exe` on Mac, `/bin/zsh` on Windows) to
the OS default at load time, so `terminal-settings.json` files survive
moving a project between hosts. `TerminalSessionManager` injects a
zsh `ZDOTDIR` override (Mac: `~/Library/Application Support/Concord/zsh`)
and a bash rcfile that prepends `/opt/homebrew/{bin,sbin}` and
`/usr/local/bin` to PATH so `claude`, `codex`, and `gh` resolve out of
the box without the user's `.zshrc` having loaded yet.

**Action bridge (`src/StudioProActionServer.cs`).** Switched from
`HttpListener` to a hand-rolled HTTP/1.1 dispatcher over `TcpListener`
(~150 LOC). HttpListener on macOS does not properly isolate prefixes
by port — probes to `localhost:55169` were being answered by Studio
Pro's own HttpListener on `7782` with a `Microsoft-NetCore/2.0`
404 — and HttpListener is also not officially supported on Mac by
.NET. TcpListener is cross-platform, well-supported, and our HTTP
needs are tiny (POST `/mcp`, JSON in/out). The four hotkey-based
tools (`run_app` / `stop_app` / `refresh_project` / `save_all`) silently
no-op on Mac via `OperatingSystem.IsWindows()` guards in
`StudioProUiAutomation.Send` — they require Win32 `PostMessage` with
no equivalent that works on Mac without prompting for accessibility
permissions. The two service-based tools
(`get_active_run_configuration`, `get_app_status`) work on both
platforms.

**WKWebView main-thread offload (`src/TerminalSessionManager.cs`).**
Studio Pro's WKScriptMessage handler delivers JS→C# messages on the
main UI thread on Mac. Synchronously taking `WriteLock` and writing to
the PTY would block the main thread — visible as the rainbow
beachball on every keystroke. `Write(tabId, data)` now offloads the
lock-acquire + PTY write to the thread pool. Per-tab order is still
preserved by the `SemaphoreSlim` semaphore. WebView2 on Windows
happens to dispatch off-thread, which masked the issue on the original
code path; the offload helps both platforms.

**Build target (`Terminal.csproj`).** `DeployToMendix` already had
cross-platform branches; this release adds an `extensions-cache`
overlay step so Mac builds also refresh the per-project Studio Pro
snapshot at `<project>/.mendix-cache/extensions-cache/<guid>/`.
Without this, Studio Pro on Mac kept serving the cached `wwwroot/`
across iterations.

### Tests + tooling

- `StudioProThemeProbeTests.cs` — verifies path resolution branches
  cleanly to Windows (`%LOCALAPPDATA%\Mendix\Settings.sqlite`) on Win
  and to `~/Library/Application Support/Mendix/Settings.sqlite` on Mac
- Cross-platform `Spawn_Echo_ProducesExpectedOutput_CrossPlatform`
  exercises the `PtyNetFactory` dispatch end-to-end on whichever OS
  the test runs on (cmd.exe / `/bin/echo`)
- `TerminalSettings` tests now use OS-aware shell paths so the
  platform-migration logic doesn't rewrite the test fixture under us
- 95 xunit tests passing on Mac + Windows (was 88 on 1.1.1)

### Known caveats on Mac

- The four hotkey-based Action Bridge tools (`run_app`, `stop_app`,
  `refresh_project`, `save_all`) are no-ops on Mac. Use Studio Pro's
  own keyboard shortcuts directly.
- Studio Pro's per-project `.mendix-cache/extensions-cache/` snapshot
  means a manual drop-in of a new `Concord/` folder requires a full
  Studio Pro restart (or a manual cache clear) before the new bits are
  served. The developer-path build handles this automatically.

## 1.1.1 — 2026-05-02

### MCP probe + save fixes (fresh-project regression)

Two bugs surfaced when opening Concord on a fresh Mendix project that had
no prior `terminal-settings.json`:

- **Wrong default MCP port (7782).** `TerminalSettings.Defaults()` used a
  legacy port, so a fresh project's "Enable Studio Pro MCP" toggle would
  probe `localhost:7782` and time out. Default is now `8100` (Studio Pro's
  standard MCP port). The runtime always re-probes Studio Pro's actual
  port from `%LOCALAPPDATA%\Mendix\Settings.sqlite` at save time; the
  default only applies when the SQLite probe fails entirely.
- **Settings didn't persist when MCP probe failed.** Toggling "Enable
  Studio Pro MCP" and clicking Save would silently revert if the probe
  timed out. The probe failure now surfaces a notice but the save
  proceeds — the user's intent (toggle ON) is preserved; they fix the
  Studio Pro Preferences and re-save to wire up the CLI configs.

These regressions existed in 1.1.0 but were latent because the testbed
project (TestOSApp3) had a pre-existing settings file with the right port.

## 1.1.0 — 2026-05-01

### Paste pipeline overhaul

**What this means in practice:** users can now paste multi-page content
(policy docs, code blocks, chat transcripts — anything up to 1 MB)
directly into Claude Code's prompt and have it land as one paste, not
as 50+ individual submissions. When the receiving CLI supports it,
big pastes collapse to the native `[Pasted text +N lines]` placeholder
in the prompt history. Pastes ≥ 4 KB show a quiet status notice;
≥ 50 KB show an estimated delivery time; ≥ 1 MB are refused with a
"save to a file" guidance.

Multi-line paste into a CLI prompt (notably Claude Code) used to truncate
above ~30 lines on Windows. Fixed end-to-end with a four-layer approach.

**PTY backend: WinPTY → ConPTY.** Replaced `Quick.PtyNet` +
`Quick.PtyNet.WinPty` with hand-rolled `kernel32!CreatePseudoConsole`
P/Invoke (~290 LOC, `src/PtySession.cs`). ConPTY proxies VT input mode
faithfully, so modern TUI prompts (Claude Code, vim, fzf) can negotiate
bracketed-paste mode (`\x1b[?2004h`) with our xterm.js. Verified: first
`bracket-mode SET` log line ever observed across all four investigation
rounds. Side wins: no more native sidecar `winpty.dll` to deploy, no
more `AssemblyLoadContext` resolver hack for MEF load paths.

**Paced chunking with per-tab write lock.** UI chunks input ≥ 1 KB into
256-byte slices with 25 ms gaps; C# `TerminalSessionManager.Write`
serializes per session via `SemaphoreSlim`. Defense in depth for
non-bracketed receivers and very large pastes. Numbers tuned via real
measurement against Claude Code on Windows.

**LF-bypass branch for non-bracketed receivers.** When bracketed-paste
mode is OFF and the paste contains newlines, bypass xterm's default
`\r?\n → \r` coercion (which causes line-aware prompts to treat each
newline as Enter/submit) and stream LFs directly via the keystroke
channel.

**Size-tiered UX.** Pastes ≥ 4 KB show a brief notice; ≥ 50 KB show a
duration estimate; ≥ 1 MB are refused with a "save to file" hint. New
shared `notice.ts` helper lets any UI component surface the chrome
banner (previously private to settings-modal).

### Tests + tooling

- New pure helpers in `ui/src/paste.ts` (line-ending normalization, size
  classifier, chunk range generator, duration estimator, line counter)
- 26 new vitest tests covering all five helpers (33 UI tests total)
- 2 new xunit tests in `tests/TerminalSessionManagerWriteLockTests.cs`
  proving per-session write serialization without cross-session
  blocking (88 C# tests total)
- Manual paste regression matrix added to `DEPLOYING.md`
- Architecture + diagnostic playbook in `docs/PASTE.md`

### Diagnostics

- Output stream scanner detects `\x1b[?2004h` / `?2004l` and logs
  `bracket-mode SET` / `bracket-mode RESET` per tab
- Paste handler logs `bracketed=`, MIME types, plain length (shape only,
  never content — clipboard secrets risk)
- Per-keystroke input log gated behind `bytes > 32` (no more typing
  flood in the log)
- Removed clipboard-content preview lines from the log

### Files added

- `src/PtySession.cs` (full rewrite for ConPTY)
- `ui/src/paste.ts`, `ui/src/paste.test.ts`
- `ui/src/notice.ts`
- `tests/TerminalSessionManagerWriteLockTests.cs`
- `docs/PASTE.md`
- `CHANGELOG.md` (this file)

### Files removed from deploy

- `winpty.dll`, `winpty-agent.exe` (no longer needed)
- 7 fewer files in the deployed extension folder

## 1.0.0 — 2026-04-30

Initial Concord release (renamed from "Terminal" / "mxTerminal"). See
git history before this changelog entry for the rename + visual identity
work.
