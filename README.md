# Concord

> *"The terminal Studio Pro was missing."*

**Current version: 1.2.0** ([CHANGELOG](./CHANGELOG.md)) — **macOS support**. Concord now runs on both Windows and Mac: hand-rolled POSIX PTY backend (`openpty` + `posix_spawnp`) that mirrors the ConPTY surface, WKWebView bridge for Studio Pro on Mac, Settings.sqlite probe under `~/Library/Application Support/Mendix/`, Homebrew-aware shell init, and platform-aware shell-path migration when projects move between OS hosts.

Concord is a Mendix Studio Pro 11.10+ extension (Windows and macOS) that embeds a tabbed terminal as a dockable pane. The pane is the workspace where you run **Claude Code**, **Codex**, or **GitHub Copilot CLI** — and they talk directly to:

- **Studio Pro's built-in MCP server** (model-tier — entities, microflows, pages, OQL, file ops on `/themes` + `/jsactions`, knowledge)
- **Maia** (Studio Pro's in-IDE AI assistant)
- **Concord's own Action Bridge** (UI-tier — run / stop / refresh / save / project status)

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

### Action Bridge — what Studio Pro's MCP can't do

A second MCP server inside Concord (port 7783 by default; auto-fallback to a free port if 7783 is taken). Exposes UI-tier operations to the CLIs above as MCP tools:

| Tool | Effect | Mechanism |
|---|---|---|
| `run_app` | Start the local Mendix runtime | F5 hotkey to Studio Pro main window |
| `stop_app` | Stop the local Mendix runtime | Shift+F5 hotkey |
| `refresh_project` | Reload the project model from disk | F4 hotkey (configurable) |
| `save_all` | Save all unsaved changes (best-effort — works when document tab has focus) | Ctrl+S hotkey |
| `get_active_run_configuration` | Return the active local run config (id, name, applicationRootUrl) | `ILocalRunConfigurationsService` |
| `get_app_status` | Composite snapshot — project path/name + run state + active config | Composite |

The first 4 use Win32 `PostMessage` to Studio Pro's main window. The last 2 read Mendix services directly. Enable the bridge in **Settings → Action bridge**.

> **macOS note:** the four hotkey-based tools (`run_app`, `stop_app`, `refresh_project`, `save_all`) silently no-op on Mac — they require Win32 `PostMessage`, which has no Mac equivalent that works without accessibility-permission prompts. The two service-based tools (`get_active_run_configuration`, `get_app_status`) work on both platforms. Run / stop / refresh on Mac: use Studio Pro's own keyboard shortcuts (F5 / Shift+F5 / F4) directly, or click the toolbar.

### Settings panel

Left-rail navigator (the Microsoft Teams pattern, not Studio Pro's deep tree). Six sections:

1. **General** — tab persistence, ring-buffer KB, scrollback lines.
2. **Shell** — shell selector (auto-detected list), launch arguments.
3. **Studio Pro MCP** — enable + per-CLI client list (Claude Code, Copilot CLI, Codex).
4. **Action bridge** — enable + refresh-from-disk hotkey.
5. **Skills** — placeholder. Coming feature: install prescriptive skill packs that Concord writes into your Mendix project tree to teach Studio Pro patterns it doesn't ship with.
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
src/                    C# extension code (MEF, action server, theme probe)
src/PtySession.cs       ConPTY backend — Windows (kernel32!CreatePseudoConsole)
src/UnixPtySession.cs   POSIX PTY backend — macOS (openpty + posix_spawnp via libSystem)
ui/src/                 TypeScript UI (xterm.js, settings modal, bridge, icons, logo)
ui/src/bridge.ts        WebView2 (Windows) and WKWebView (Mac) transport
ui/index.html           Single-page UI bundled into the extension
tests/                  xunit test suite
docs/superpowers/       Original design docs (specs + plans)
manifest.json           Mendix extension manifest — points at Concord.dll
Terminal.csproj         Project file (assembly name = Concord)
```

---

## License

Apache 2.0 — see [LICENSE](./LICENSE).
