# Concord

> *"The terminal Studio Pro was missing."*

Concord is a Mendix Studio Pro 11.10+ extension that embeds a tabbed terminal as a dockable pane. The pane is the workspace where you run **Claude Code**, **Codex**, or **GitHub Copilot CLI** — and they talk directly to:

- **Studio Pro's built-in MCP server** (model-tier — entities, microflows, pages, OQL, file ops on `/themes` + `/jsactions`, knowledge)
- **Maia** (Studio Pro's in-IDE AI assistant)
- **Concord's own Action Bridge** (UI-tier — run / stop / refresh / save / project status)

The result: developer + Maia + CLI agent collaborate on Mendix apps from one workspace, with Concord wiring the integration plumbing automatically.

A Siemens **OneSource Center of Excellence** extension.

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
- Default shell `powershell.exe`; pick from detected shells (`bash`, `cmd`, etc.) in **Settings → Shell**.
- Tab names follow the format `Pwsh - 1`, `Bash - 2`, `Cmd - 3` — Title-case lowercase-canonical shell label, hyphen, gap-filling ordinal. Close `Pwsh - 2`, the next new tab fills slot `2`, not `4`.

### Persistent tabs

- On Studio Pro restart, Concord re-spawns your last session's tabs silently. State persists per-project at `<project>/resources/terminal-state.json`.
- Tabs that exited cleanly (you typed `exit`) are NOT restored. Tabs killed by a crash or by closing Studio Pro ARE restored.
- Toggle in **Settings → General → Restore tabs on reopen** (default ON).

### Theme follows Studio Pro

- Auto-matches Studio Pro's dark / light theme by reading the host's preference from `%LOCALAPPDATA%\Mendix\Settings.sqlite` at pane open. No setting to keep in sync.
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

### Settings panel

Left-rail navigator (the Microsoft Teams pattern, not Studio Pro's deep tree). Six sections:

1. **General** — tab persistence, ring-buffer KB, scrollback lines.
2. **Shell** — shell selector (auto-detected list), launch arguments.
3. **Studio Pro MCP** — enable + per-CLI client list (Claude Code, Copilot CLI, Codex).
4. **Action bridge** — enable + refresh-from-disk hotkey.
5. **Skills** — placeholder. Coming feature: install prescriptive skill packs that Concord writes into your Mendix project tree to teach Studio Pro patterns it doesn't ship with.
6. **About** — version, log file path, settings file path, the OneSource CoE logo (hover to spin).

Modal title: "Concord Terminal Settings". Footer credit on every section: "A Siemens CoE extension for Studio Pro."

---

## Logs

`<project>/resources/terminal.log` — thread-safe per-line append log. INFO / WARN / ERROR. Captures extension lifecycle, action server start/stop, MCP probe results, paste diagnostics. Path also visible in **Settings → About → Log file**.

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
src/                    C# extension code (MEF, Pty.Net, action server, theme probe)
ui/src/                 TypeScript UI (xterm.js, settings modal, bridge, icons, logo)
ui/index.html           Single-page UI bundled into the extension
tests/                  xunit test suite
docs/superpowers/       Original design docs (specs + plans)
manifest.json           Mendix extension manifest — points at Concord.dll
Terminal.csproj         Project file (assembly name = Concord)
```

---

## License

[TBD — set by the owner.]
