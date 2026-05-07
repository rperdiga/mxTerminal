# Changelog

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
