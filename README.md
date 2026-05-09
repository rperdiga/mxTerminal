# Concord

> *"The terminal Studio Pro was missing."*

**Current version: 4.0.0** ([CHANGELOG](./CHANGELOG.md)) — **Bundled Mendix skill packs**. Concord ships 7 prescriptive Mendix skill packs that install into your project's `.claude/skills/`, `.github/skills/`, or `.codex/skills/` per the CLIs you enable. Combined with the renamed Concord MCP server (Studio Pro UI actions + Maia integration housed under one HTTP endpoint), the CLI agent in your terminal is ready to drive Studio Pro from day one.

Concord is a Mendix Studio Pro 11.10+ extension (Windows and macOS) that embeds a tabbed terminal as a dockable pane. The pane is the workspace where you run **Claude Code**, **Codex**, or **GitHub Copilot CLI** — and they talk directly to:

- **Studio Pro's built-in MCP server** (model-tier — entities, microflows, pages, OQL, file ops on `/themes` + `/jsactions`, knowledge)
- **Concord MCP** — Concord's own in-process MCP server with two tool families: Studio Pro UI actions (run / stop / refresh / save / status) and Maia integration (programmatic access to Studio Pro's in-IDE AI assistant)
- **Bundled Mendix skill packs** — prescriptive playbooks installed into the project so the CLIs know how to drive the two MCP servers above

The result: developer + Maia + CLI agent collaborate on Mendix apps from one workspace, with Concord wiring the integration plumbing automatically.

A **Siemens CoE Team** extension.

---

## Install / deploy → see [DEPLOYING.md](./DEPLOYING.md)

That doc covers both paths:

- **Developer path** — clone this repo, `dotnet build`, deploy to one or many Mendix projects.
- **Consumer path** — copy a prebuilt `extensions/Concord/` folder into any Mendix project's `extensions/` directory. No build required.

It also covers migrating from the old "Terminal" extension (clean up the orphan folder so Studio Pro doesn't load both).

---

## What you get

### Tabbed terminal

- Multiple PTY tabs per pane. Each tab spawns a real shell rooted at the open Mendix project's directory.
- Default shell — Windows: `powershell.exe`; macOS / Linux: your `$SHELL` (typically `zsh`), with `/bin/zsh` and `/bin/sh` as fallbacks. Pick from detected shells (`bash`, `cmd`, `pwsh`, etc.) in **Settings → Shell**.
- On macOS, Homebrew (`/opt/homebrew/bin`, `/usr/local/bin`) is prepended to the shell's PATH so `claude`, `codex`, and `gh` resolve out of the box without your `.zshrc` having run yet.
- Tab names follow the format `Pwsh - 1`, `Bash - 2`, `Cmd - 3` — Title-case lowercase-canonical shell label, hyphen, gap-filling ordinal. Close `Pwsh - 2`, the next new tab fills slot `2`, not `4`.

### Persistent tabs

- On Studio Pro restart, Concord re-spawns your last session's tabs silently. State persists per-project at `<project>/resources/terminal-state.json`.
- Tabs that exited cleanly (you typed `exit`) are NOT restored. Tabs killed by a crash or by closing Studio Pro ARE restored.
- Toggle in **Settings → General → Restore tabs on reopen** (default ON).

### Theme follows Studio Pro

- Auto-matches Studio Pro's dark / light theme by reading the host's preference from Studio Pro's `Settings.sqlite` at pane open. No setting to keep in sync.
  - Windows: `%LOCALAPPDATA%\Mendix\Settings.sqlite`
  - macOS: `~/Library/Application Support/Mendix/Settings.sqlite`
- The pane chrome inherits Studio Pro's exact surfaces; the xterm canvas blends seamlessly with the pane background.
- Same restart-to-apply behavior as Studio Pro itself: change theme in **Edit → Preferences**, restart Studio Pro, the terminal follows.

### Studio Pro MCP integration

- **Settings → Studio Pro MCP** — enable to write `.mcp.json` (Claude Code, Copilot CLI) and `~/.codex/config.toml` (Codex) entries that point each CLI at Studio Pro's MCP server.
- The URL written into each config tracks Studio Pro's actual MCP port (probed live from `Settings.sqlite`). If you change the port in Studio Pro's preferences, Concord picks it up on the next Save in the modal — no port to keep in sync manually.

### Concord MCP — what Studio Pro's MCP can't do

A second MCP server inside Concord (server name `concord-mcp`, port 7783 by default; auto-fallback to a free port if 7783 is taken). Two tool families share the endpoint:

**Studio Pro UI actions** — UI-tier operations the CLI can drive without leaving the terminal:

| Tool | Effect | Mechanism |
|---|---|---|
| `run_app` | Start the local Mendix runtime | F5 hotkey to Studio Pro main window |
| `stop_app` | Stop the local Mendix runtime | Shift+F5 hotkey |
| `refresh_project` | Reload the project model from disk | F4 hotkey (configurable) |
| `save_all` | Save all unsaved changes (best-effort — works when document tab has focus) | Ctrl+S hotkey |
| `get_active_run_configuration` | Return the active local run config (id, name, applicationRootUrl) | `ILocalRunConfigurationsService` |
| `get_app_status` | Composite snapshot — project path/name + run state + active config | Composite |

The first 4 use Win32 `PostMessage` to Studio Pro's main window. The last 2 read Mendix services directly.

**Maia integration** (Windows only) — programmatic access to Studio Pro's in-IDE AI assistant via Chrome DevTools Protocol against Studio Pro's WebView2 panel:

| Tool | Effect |
|---|---|
| `maia__send` | Push a prompt into Maia's chat input |
| `maia__status` | Read Maia's current state (idle / thinking / responding) |
| `maia__wait` | Block until Maia finishes responding |
| `maia__ask` | Send + wait + return the response in one call |
| `maia__reset` | Clear Maia's conversation |
| `maia__force_tier` | Override the transport tier (debug aid) |

Two-tier transport: injected JS agent (Tier 1, fast) with DOM-scrape fallback (Tier 2). Both use Studio Pro's `--remote-debugging-port`; the Maia panel must be visible while these tools are in use.

Enable both families in **Settings → Concord MCP** (sub-toggles for each).

> **macOS:** the four hotkey-based UI-action tools work on Mac via `osascript` driving System Events to keystroke Studio Pro (identified by Unix PID, so the `.app` display name doesn't matter). One-time setup: macOS prompts for Accessibility permission the first time Concord MCP runs — open **System Settings → Privacy & Security → Accessibility** and enable Studio Pro. Until you grant it, the calls fail with a clear "Accessibility permission not granted" message that Claude can relay to you. The two service-based tools (`get_active_run_configuration`, `get_app_status`) work on both platforms with no permissions needed. **Maia integration is Windows-only in this release**; the toggle appears disabled on Mac.

### Settings panel

Left-rail navigator (the Microsoft Teams pattern, not Studio Pro's deep tree). Six sections:

1. **General** — tab persistence, ring-buffer KB, scrollback lines.
2. **Shell** — shell selector (auto-detected list), launch arguments.
3. **Studio Pro MCP** — enable + per-CLI client list (Claude Code, Copilot CLI, Codex).
4. **Concord MCP** — enable Concord's in-process MCP server. Two sub-toggles: **Studio Pro UI actions** (run / stop / refresh / save / status tool family) and **Maia integration** (Maia tool family, Windows-only — disabled on macOS). Refresh-from-disk hotkey is configurable here.
5. **Skills** — install bundled Mendix skill packs into the open project. Master toggle + per-CLI checkboxes (Claude Code → `.claude/skills/`, Copilot CLI → `.github/skills/`, Codex → `.codex/skills/`). Each Save refreshes the bundled folders so a Concord upgrade ships new skills automatically; user-authored skills sitting alongside in the same directory are left intact.
6. **About** — version, log file path, settings file path, the CoE Team logo (hover to spin).

Modal title: "Concord Terminal Settings". Footer credit on every section: "A Siemens CoE extension for Studio Pro."

---

## Logs

`<project>/resources/terminal.log` — thread-safe per-line append log. INFO / WARN / ERROR. Captures extension lifecycle, action server start/stop, MCP probe results, paste diagnostics. Path also visible in **Settings → About → Log file**.

---

## Paste handling

**What it means for you:** paste a 50-line policy doc, a 5 KB code block, or a multi-page chat transcript directly into Claude Code's prompt. The whole thing lands as one paste — not 50 individual submissions, not truncated to the tail. When the receiving CLI supports it (Claude Code on this PTY backend does), pastes ≥ a few lines collapse to the native `[Pasted text +N lines]` placeholder so your prompt history stays scannable. Big pastes (≥ 4 KB) get a quiet status notice; very big ones (≥ 50 KB) show an estimated delivery time; anything ≥ 1 MB is refused with a "save to a file and read it" suggestion.

**Why it works:** multi-line paste into a CLI prompt requires special care. xterm.js's default behavior collapses every newline to a bare CR, which line-aware prompts (Claude Code, vim, multi-line PSReadLine) interpret as Enter — turning a 30-line paste into 30 separate submissions and overflowing the input buffer. Concord routes paste based on whether the running CLI has enabled bracketed-paste mode (`\x1b[?2004h`):

- **Bracketed-paste ON** → atomic round-trip via xterm.js's normal path; the CLI receives the whole paste between `\x1b[200~ ... \x1b[201~` markers
- **Bracketed-paste OFF + multi-line text** → bypass xterm; send LF-normalized bytes through the keystroke channel so prompts treat newlines as line-continuation, not submit

The PTY backend negotiates bracketed-paste end-to-end on both platforms: ConPTY (`kernel32!CreatePseudoConsole`) on Windows, and `openpty` + `posix_spawnp` against `libSystem.dylib` on macOS. The previous WinPTY backend on Windows silently dropped the negotiation handshake; both current backends proxy DECSET/DECRST sequences faithfully.

Full design rationale + diagnostic playbook: [docs/PASTE.md](./docs/PASTE.md).

---

## Development

See [DEPLOYING.md § Developer path](./DEPLOYING.md#developer-path-build-from-source) for the full build + iterate loop.

Quick reference:

```sh
# Configure deploy target (one-time)
copy Directory.Build.props.example Directory.Build.props
# edit MendixDeployTarget to your project root

# Build + deploy
dotnet build

# Test
dotnet test
```

The csproj's `BuildUi` target runs `npm install` (first build only) + `node esbuild.mjs` to bundle the xterm.js TypeScript into `wwwroot/terminal.bundle.js`. The `DeployToMendix` target then `xcopy`s the build output into each `MendixDeployTarget`'s `extensions/Concord/` directory.

---

## Project layout

```
src/                    C# extension code (MEF, Concord MCP server, theme probe)
src/PtySession.cs       ConPTY backend — Windows (kernel32!CreatePseudoConsole)
src/UnixPtySession.cs   POSIX PTY backend — macOS (openpty + posix_spawnp via libSystem)
src/Maia/               Maia bridge — CDP transports + tool registrations
src/SkillInstaller.cs   Per-CLI bundled-skill install/uninstall (.claude/.github/.codex)
ui/src/                 TypeScript UI (xterm.js, settings modal, bridge, icons, logo)
ui/src/bridge.ts        WebView2 (Windows) and WKWebView (Mac) transport
ui/index.html           Single-page UI bundled into the extension
skills/                 7 bundled Mendix skill packs (microflow, page, view-entity, workflow patterns)
tests/                  xunit test suite
docs/superpowers/       Original design docs (specs + plans)
manifest.json           Mendix extension manifest — points at Concord.dll
Terminal.csproj         Project file (assembly name = Concord)
```

---

## License

Apache 2.0 — see [LICENSE](./LICENSE).
