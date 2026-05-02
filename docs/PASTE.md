# Paste handling in Concord

## Architecture (current as of 2026-05-01)

Concord's PTY backend is **ConPTY** (Windows 10 1809+ pseudoconsole),
implemented as hand-rolled kernel32 P/Invoke in `src/PtySession.cs`.
This replaced WinPTY in Round 4 of the paste investigation; ConPTY
proxies VT input mode (`ENABLE_VIRTUAL_TERMINAL_INPUT`) faithfully so
modern TUI prompts (Claude Code, vim, fzf) can negotiate bracketed-paste
mode end-to-end.

When the running CLI emits `\x1b[?2004h`, our `tab-manager.ts` output
scanner logs `bracket-mode SET` and xterm.js wraps subsequent pastes
in proper `\x1b[200~ ... \x1b[201~` markers. The CLI receives the
paste as one atomic block, not a stream of keystrokes.

## TL;DR

Concord's paste pipeline is designed to do the right thing whether the
running CLI supports bracketed-paste mode or not.

- **App opted into bracketed-paste mode** (`\x1b[?2004h` was emitted by
  the CLI): xterm.js handles the paste atomically. Newlines round-trip
  inside `\x1b[200~ ... \x1b[201~` markers. This is the happy path.
- **App did NOT opt in**: we bypass `term.paste()` to avoid xterm's
  default `\r?\n` → `\r` collapsing. We send the text via the same
  byte channel as keystrokes, with `\r\n?` normalized to `\n`. Modern
  TUI prompts treat LF as line-continuation; the user explicitly
  presses Enter to submit.

The branch lives in `ui/src/xterm-tab.ts` in the document-level paste
handler.

## Why we don't always trust `term.paste()`

xterm.js's `prepareTextForTerminal` runs `text.replace(/\r?\n/g, '\r')`
unconditionally. That's correct ONLY when bracketed-paste mode is on,
because the receiving CLI then knows to buffer everything between the
`200~` start and `201~` end markers and treat the bytes inside as data,
not keystrokes.

When bracketed-paste mode is OFF (e.g. older CLIs, or — as we observed
2026-05-01 — Claude Code on Windows in some PTY configurations), each
bare `\r` is interpreted by line-aware prompts as Enter/submit. A
30-line paste then becomes 30 separate submit events. Receiving
prompts that buffer input (Ink/React-based agents like Claude Code,
PSReadLine in continuation-line mode) drop most of the content because
their input pipeline can't keep up — only the tail survives.

See `~/.claude/projects/C--Workspace-Dev-Projects-mxTerminal/journal/2026-05-01-paste-investigation.md`
for the full root-cause walkthrough.

## What's logged for diagnostics

Two probes write to the per-project log
(`<project>/resources/terminal.log`):

- `tab-manager.ts` scans every PTY-output chunk for `\x1b[?2004h` and
  `\x1b[?2004l`. When found, logs `bracket-mode SET` / `bracket-mode
  RESET` with the tab id. Tells you whether and when the running CLI
  toggled bracketed-paste mode.
- `xterm-tab.ts` paste handler logs, on every paste:
  - `bracketed=<true|false|unknown>` — current mode at moment of paste
  - `types=[...]` — all clipboard MIME types present
  - `plainLen=N htmlLen=N` — text size on each MIME slot
  - `plainPreview=...` (first 200 chars, with `\r` and `\n` shown as
    literals so line-ending semantics are visible)
  - `paste bypass-CR-coercion ...` line when the LF-bypass branch fires

These logs are intentionally verbose so future paste regressions can be
diagnosed from a single user report. They do NOT log keystroke-level
input (the C# input log is gated behind `bytes.Length > 32`).

## Paced chunking for large pastes

Even with the LF-bypass branch correctly delivering all bytes to the PTY
in one write, large pastes (~30+ lines / >1KB) into Node/Ink-based TUI
agents like Claude Code on Windows still truncate — head bytes lost,
tail survives. This is a fixed-size stdin ring-buffer overrun inside
the receiving CLI's input tokenizer (canonical:
[claude-code #49337]). It reproduces on macOS/Linux native PTYs too,
so it's not a Windows or WinPTY-specific problem.

Concord mitigates by pacing: when an input message is ≥ 1024 bytes, we
slice into 512-byte chunks and `await setTimeout(10ms)` between
`bridge.send` calls. This gives the receiving CLI's input loop time to
drain its ring buffer between bursts. Numbers come from VS Code's
terminal-process tuning ([microsoft/vscode #292058] proposed fix:
"safe size for shell stdin").

The C# side has a per-session `SemaphoreSlim` (`SessionState.WriteLock`)
that serializes paced chunks to the PTY writer so they can't interleave
even though they arrive on separate bridge dispatch threads. Different
tabs still write in parallel; only same-tab chunks serialize.

Constants live at the top of `ui/src/tab-manager.ts`:

- `PACED_CHUNK_THRESHOLD = 1024`
- `PACED_CHUNK_BYTES = 512`
- `PACED_CHUNK_DELAY_MS = 10`

Each paced send logs `paced-input tab=... bytes=... chunks=...
elapsed=...ms` for tuning. Re-measure if you change the constants.

[claude-code #49337]: https://github.com/anthropics/claude-code/issues/49337
[microsoft/vscode #292058]: https://github.com/microsoft/vscode/issues/292058

## What we ruled out (and why) — kept here so we don't reinvestigate

- **`text/html`-only Teams clipboard fallback.** Microsoft Teams chat
  copy can produce HTML-only clipboards in some apps, but in our
  WebView2 host with the failing 2026-05-01 paste, `text/plain` was
  present (2383 chars). If a future failing paste shows
  `plainLen=0 htmlLen>0`, re-open this — needs a DOM-walker that emits
  `\n` at block boundaries (`<div>`, `<p>`, `<br>`, `<li>`, `<tr>`),
  NOT `.textContent` (which collapses Teams' per-line `<div>`s).
- **16 KB chunking race.** WebView2 `postMessage` and the C# message
  dispatcher are single-threaded FIFO; chunks can't reorder. The
  failing 2.4 KB paste was below the chunk threshold anyway.
- **C# `TerminalSessionManager.Write` write contention.** Synchronous
  write from one dispatcher thread; no race observed. A SemaphoreSlim
  would be cheap insurance if multi-thread callers ever land, but the
  symptom didn't require it.
- **Claude Code <2.1.108 bracketed-paste regression.** User runs 2.1.126.

## When you change paste behavior

Run the manual matrix:

| Source        | Target        | Expected                                  |
| ------------- | ------------- | ----------------------------------------- |
| Notepad       | PowerShell    | Multi-line paste; PSReadLine bracketed-OK |
| Notepad       | Claude Code   | Multi-line paste lands as one prompt      |
| Teams chat    | PowerShell    | Multi-line paste; bracketed when on       |
| Teams chat    | Claude Code   | Multi-line paste lands as one prompt      |
| VS Code edit  | PowerShell    | Multi-line, no extra blank lines          |
| Single line   | any           | Submits as single line (no LF added)      |

Capture the `bracketed=...` log line from each test paste and compare
to the prior run.
