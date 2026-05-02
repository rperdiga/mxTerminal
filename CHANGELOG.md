# Changelog

## 1.1.0 — 2026-05-01

### Paste pipeline overhaul

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
