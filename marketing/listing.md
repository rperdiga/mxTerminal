# Concord — Marketplace listing copy

Reference for the Mendix Marketplace Publish Component form.
Copy/paste sections into the matching form fields.

---

## Component name (max 50 chars)

```
Concord — Terminal for Studio Pro
```

(34 chars — under the 50 limit.)

## Component type

**Module** (Studio Pro Extensions are published as add-on modules per
[Mendix docs](https://docs.mendix.com/appstore/modules/).)

## Visibility

**Public** (under MxLabs publisher; ask MxLabs admin if you need
publisher-private during draft review.)

## Short pitch (≤140 chars — for the tile / search results)

```
A real terminal pane for Studio Pro. Run Claude Code, Codex, or Copilot CLI wired to Studio Pro MCP, Maia, and bundled skill packs.
```

(133 chars — comfortably under the 140 limit.)

## Long description (Markdown supported; goes in the Content step)

```markdown
**Concord is the terminal Studio Pro was missing.**

A tabbed PTY terminal embedded as a dockable pane inside Studio Pro
11.10+ on Windows and macOS. The pane is the workspace where
developers run modern CLI agents — **Claude Code**, **Codex**,
**GitHub Copilot CLI** — and those agents talk directly to:

- **Studio Pro's built-in MCP server** (entities, microflows, pages,
  OQL, file ops on `/themes` + `/jsactions`, knowledge base)
- **Concord MCP** — Concord's own in-process MCP server with two
  tool families: Studio Pro UI actions (run / stop / refresh / save /
  status) and Maia integration (programmatic access to Studio Pro's
  in-IDE AI assistant)
- **Bundled Mendix skill packs** — prescriptive skill packs installed
  into the project so the CLIs know how to drive the two MCP servers
  above

The result: developer + Maia + CLI agent collaborate on Mendix apps
from one workspace, with Concord wiring the integration plumbing
automatically.

## Why Concord exists

Studio Pro 11 ships an extensibility API but no terminal of its own.
Modern Mendix workflows increasingly involve AI agent CLIs (Claude
Code, Codex, Copilot CLI) that need a real PTY to run inside —
xterm-grade rendering, a working clipboard, paste-aware input
handling, theme-matched chrome. Without that, developers
context-switch between Studio Pro and a separate terminal app every
time the agent needs to be invoked, and the integration between
Studio Pro's MCP server and the running agent has to be wired up
by hand on every project.

Concord makes the agent-augmented Mendix workflow first-class.

## What's in the pane

### Tabbed PTY terminal
- Multiple tabs per pane. Each tab spawns a real shell rooted at the
  open Mendix project's directory.
- Default shell **PowerShell** on Windows, your `$SHELL` (typically
  `zsh`) on macOS; pick from detected shells (`bash`, `cmd`, etc.)
  in **Settings → Shell**.
- Tab names follow `Pwsh - 1`, `Bash - 2`, `Cmd - 3` — gap-filling
  ordinals so the visible numbers stay tight.

### Persistent tabs
- On Studio Pro restart, Concord re-spawns your last session's tabs
  silently. State persists per-project at
  `<project>/resources/terminal-state.json`.
- Tabs that exited cleanly are NOT restored. Tabs killed by a crash
  or by closing Studio Pro ARE restored.

### Theme follows Studio Pro
- Auto-matches Studio Pro's dark / light theme by reading the host's
  preference from `%LOCALAPPDATA%\Mendix\Settings.sqlite` (Windows)
  or `~/Library/Application Support/Mendix/Settings.sqlite` (macOS)
  at pane open. No setting to keep in sync.
- The pane chrome inherits Studio Pro's exact surfaces; the xterm
  canvas blends seamlessly with the pane background.

### Studio Pro MCP integration
- **Settings → Studio Pro MCP** — enable to write `.mcp.json`
  (Claude Code, Copilot CLI) and `~/.codex/config.toml` (Codex)
  entries that point each CLI at Studio Pro's MCP server.
- One toggle, three CLI configs in sync.
- **Codex is opt-in only.** Auto-enabling it would write to
  user-global `~/.codex/config.toml` — outside the project tree.
  Tick it explicitly in Settings if you use Codex.

### Concord MCP
- A second MCP server hosted by Concord on port 7783 (auto-fallback
  to a free port if 7783 is taken), wire identity `concord-mcp`. Two
  tool families share the endpoint:
  - **Studio Pro UI actions** — `run_app`, `stop_app`,
    `refresh_project`, `save_all`, `get_active_run_configuration`,
    `get_app_status`. Drive the IDE itself, not just the model.
  - **Maia integration** (Windows only) — `maia__send`, `maia__status`,
    `maia__wait`, `maia__ask`, `maia__reset`, `maia__force_tier`.
    Programmatic access to Studio Pro's in-IDE AI assistant.
- Master + per-family toggles in **Settings → Concord MCP**.

### Bundled Mendix skill packs
- 7 prescriptive Mendix skill packs ship with the extension —
  microflow creation/editing, pages, view entities, workflow
  patterns. Each one is a `SKILL.md` with YAML frontmatter that the
  CLIs auto-discover:
  - `mendix-microflow-common` · `mendix-microflow-syntax` · `mendix-microflow-update`
  - `mendix-page-gen` · `mendix-view-entities`
  - `mendix-workflow-common` · `mendix-workflow-update`
- Toggle per-CLI in **Settings → Skills**: Concord installs the
  bundled folders into `<project>/.claude/skills/`,
  `<project>/.github/skills/`, or `<project>/.codex/skills/`.
  Disabling a CLI removes only Concord's bundled folders;
  user-authored siblings under the same directory stay intact.

### Paste pipeline that actually works

Paste a 50-line policy doc, a 5 KB code block, or a multi-page chat
transcript directly into Claude Code's prompt. The whole thing lands
as one paste — not 50 individual submissions, not truncated to the
tail. Big pastes collapse to Claude Code's native `[Pasted text +N
lines]` placeholder so your prompt history stays scannable. Pastes
≥ 4 KB show a quiet notice; ≥ 50 KB show an estimated delivery time;
≥ 1 MB are refused with a "save to a file" suggestion.

Under the hood, Concord ships a four-layer paste pipeline that fixes
the multi-line paste truncation that affects every Node/Ink-based TUI
agent on Windows ([claude-code #49337](https://github.com/anthropics/claude-code/issues/49337) and siblings):

1. **ConPTY backend** — hand-rolled `kernel32!CreatePseudoConsole`
   P/Invoke replaces WinPTY. Bracketed-paste mode now negotiates
   end-to-end with Claude Code.
2. **Paced chunking** with per-tab write lock — defense in depth
   for non-bracketed receivers and very large pastes.
3. **LF-bypass branch** for receivers that don't enable
   bracketed-paste (cmd.exe, older CLIs).
4. **Size-tiered UX** — notice ≥ 4 KB, warn + duration estimate
   ≥ 50 KB, refuse with "save to file" guidance ≥ 1 MB.

## What's new in 4.1.2

See [CHANGELOG.md](https://github.com/rperdiga/mxTerminal/blob/main/CHANGELOG.md)
for the complete history.

**4.1.2 — port-leak fix + atomic writes + cleaner copy.** The settings
modal used to round-trip the live Concord MCP port back into the saved
file as if it were configuration intent — so a one-time port-busy event
on your machine could permanently inject a phantom port like `8099` into
`terminal-settings.json`. Removed the field from the schema entirely;
the live port now surfaces only as a read-only display value.
File writes hardened with NTFS journaled rename (`File.Replace`).
Corrupt settings files are renamed `terminal-settings.json.broken-{ts}.bak`
instead of silently defaulting. Banner copy rewritten to read like a
product, not log lines. Save vs. Cancel buttons differentiated in dark
mode. Modal title trimmed to "Concord Settings".

**4.1.1 — upgrade auto-apply.** First open after a Concord upgrade
re-applies the wiring defaults (MCP servers + bundled skills) to disk
without needing to open Settings and Save. Runtime preferences (shell,
theme, ring buffer, scrollback, restore-tabs, refresh hotkey) are
preserved. Cross-machine safe: never downgrades wiring on a colleague
who pulls a project last edited on a newer Concord.

**4.1.0 — default-on settings + first-run auto-apply.** New install on
a fresh project writes `.mcp.json`, installs bundled skill packs into
`.claude/skills/` and `.github/skills/`, and persists the settings file
in one go — no modal clicks required. Three notice banners explain what
just happened; Codex remains opt-in.

**4.0.0 — bundled Mendix skill packs.** 7 prescriptive skill packs
install into your project per the CLIs you enable. Each Save refreshes
the bundled content; user-authored skills sitting alongside stay intact.

**1.3.0 — Concord MCP + Maia bridge.** The in-process MCP server
(formerly "Action Bridge") now hosts two tool families under a single
`concord-mcp` endpoint: Studio Pro UI actions (the original six) and
Maia integration (`maia__send` / `status` / `wait` / `ask` / `reset` /
`force_tier` — Windows only). Maia is C#-native — no Python, no
subprocess, no second port; two-tier transport over Studio Pro's
WebView2 `--remote-debugging-port`.

**1.2.x — macOS support.** POSIX PTY backend, WKWebView bridge, Mac
`Settings.sqlite` probe, Homebrew-aware shell init.

**1.1.x — Windows paste pipeline.** ConPTY backend with
bracketed-paste negotiation, paced chunking, LF-bypass, size-tiered UX
for very large pastes.

## Privacy / data

Concord is loopback-only. The Concord MCP server binds to
`127.0.0.1`. Maia integration uses Studio Pro's local
WebView2 `--remote-debugging-port`. Nothing leaves your machine.
The bundled skill packs are static `SKILL.md` files copied into your
project — no telemetry, no callbacks. Your CLI agents (Claude Code,
Codex, Copilot CLI) follow their own privacy contracts; Concord just
wires them to local Mendix tooling.

## Compatibility

- **Studio Pro:** 11.10.0 or newer
- **Operating system:**
  - **Windows 10 1809 (build 17763)** or newer — ConPTY is part of
    `kernel32` from this release
  - **macOS 10.15 (Catalina)** or newer — `posix_spawn_file_actions_addchdir_np`
    available from this release
- **.NET runtime:** 8.0 (bundled with Studio Pro 11)

## Documentation

- **README + architecture:** https://github.com/rperdiga/mxTerminal
- **Paste pipeline rationale:** [docs/PASTE.md](https://github.com/rperdiga/mxTerminal/blob/main/docs/PASTE.md)
- **Deploying / building from source:** [DEPLOYING.md](https://github.com/rperdiga/mxTerminal/blob/main/DEPLOYING.md)
- **Changelog:** [CHANGELOG.md](https://github.com/rperdiga/mxTerminal/blob/main/CHANGELOG.md)

## License

Apache 2.0. Source available at https://github.com/rperdiga/mxTerminal.

## Built by

The **Siemens CoE Team**. Published via **MxLabs**.
```

## Authors (form fields)

Internal contact + reporting handled via the GitHub repository's
issue tracker:
[github.com/rperdiga/mxTerminal/issues](https://github.com/rperdiga/mxTerminal/issues).

| Field            | Value                          |
| ---------------- | ------------------------------ |
| First author     | Ricardo Perdigao               |
| Second author    | Kelly Seale                    |

## License (form field)

Apache License 2.0 — link to `LICENSE` file in the repo or paste full
text if marketplace requires it inline.

## Source / GitHub URL

```
https://github.com/rperdiga/mxTerminal
```

Use the **GitHub Link** option on the upload form. Concord's release
flow attaches `Concord.mxmodule` to the `v4.1.2` GitHub release tag
(or whichever version is current); marketplace auto-syncs from the
release attachment.

## Thumbnail (600×420 PNG, ≤1 MB)

`marketing/concord-thumbnail-600x420.png`

## Screenshots (≤10 images, 600×420 PNG / JPG / SVG)

To be captured by Neo. See `marketing/SCREENSHOTS.md` for the shot
list with framing instructions.
