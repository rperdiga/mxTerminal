# Changelog

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
