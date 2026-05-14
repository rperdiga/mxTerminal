# Concord

> *"The terminal Studio Pro was missing."*

**Current version: 4.2.2** ([CHANGELOG](./CHANGELOG.md)) — **TOML hygiene + crash-safety cadence.** Two fixes and two rule sharpenings driven by the first production Codex run on v4.2.1: (1) the v1.3.0-migration TOML cleanup now strips orphan `[mcp_servers.<name>.tools.<X>]` child sub-sections that previously survived parent removal — Codex 0.128+ refuses to start when those orphans are present, so any user with a pre-v1.3.0 Concord install + Codex enabled was hard-blocked until they edited the TOML by hand. v4.2.2 fixes this automatically on the next Concord Save. (2) Codex's *"External agent config detected"* migration prompt is now suppressed for Concord-managed projects — Concord stamps a per-project future-dated entry in `~/.codex/config.toml` so Codex stops offering to migrate Claude config across (Concord already configured Codex). (3) §2 Maia recovery ladder sharpened: `maia__reset` is for recovering FROM observed failure, not for prophylactic bridge hygiene. Empirically: Codex called reset 51 times in two sessions despite zero bridge disconnects. (4) §12 verification gate adds a time-based `save_all` fallback (15 minutes) for visual-polish phases that don't cross natural batch boundaries — closes the crash-safety gap (2026-05-10 machine crash narrowly avoided losing 54 minutes of polish work). Builds on v4.2.1's bridge introspection toolkit + three-CLI rules parity. Combined with the Concord MCP server, the CLI agent in your terminal is ready to drive Studio Pro from day one — and crash-recovery friction is materially lower.

Concord is a Mendix Studio Pro extension (Windows and macOS) supporting **Studio Pro 10.24.13 through 11.x**[^10x] that embeds a tabbed terminal as a dockable pane. The pane is the workspace where you run **Claude Code**, **Codex**, or **GitHub Copilot CLI** — and they talk directly to:

[^10x]: Studio Pro 10.x support is a **preview** in 5.0.0-alpha.1 — a menu entry (`Extensions → Concord (10.x preview)`) is present but the terminal pane, MCP server, and full feature surface are 11.x-only for now. Full 10.x functionality is planned for the 5.x release line.

- **Studio Pro's built-in MCP server** (model-tier — entities, microflows, pages, OQL, file ops on `/themes` + `/jsactions`, knowledge)
- **Concord MCP** — Concord's own in-process MCP server with two tool families: Studio Pro UI actions (run / stop / refresh / save / status) and Maia integration (programmatic access to Studio Pro's in-IDE AI assistant)
- **Bundled Mendix skill packs** — prescriptive skill packs installed into the project so the CLIs know how to drive the two MCP servers above

The result: developer + Maia + CLI agent collaborate on Mendix apps from one workspace, with Concord wiring the integration plumbing automatically.

A **Siemens CoE Team** extension.

---

## Cross-version support

| Studio Pro version | Status | Deploy folder |
|---|---|---|
| **11.x** (11.10+) | Full — terminal pane, Concord MCP, Maia integration, skill packs | `extensions/Concord11x/` |
| **10.24.13** | Preview — menu entry only; full UI/MCP in 5.x release line | `extensions/Concord10x/` |

Each Mendix project gets **one** of the two folders, never both. Deploying both to the same project will crash Studio Pro of either version when it tries to load the wrong-version DLL.

The deploy folder name tells Studio Pro which host assembly to load. `Concord11x/` contains `Concord.Host11x.dll` + `Concord.Core.dll`; `Concord10x/` contains `Concord.Host10x.dll` + `Concord.Core.dll`. Each folder has its own `manifest.json` listing only the DLLs it contains.

See [DEPLOYING.md](./DEPLOYING.md) for per-host deploy target setup (`MendixDeployTarget10x` / `MendixDeployTarget11x`).

---

## Install / deploy → see [DEPLOYING.md](./DEPLOYING.md)

That doc covers both paths:

- **Developer path** — clone this repo, `dotnet build`, deploy to one or many Mendix projects.
- **Consumer path** — copy a prebuilt `extensions/Concord11x/` (or `extensions/Concord10x/`) folder into any Mendix project's `extensions/` directory. No build required.

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
| `maia__busy` | *(v4.2.1)* Read-only DOM probe — "is Maia generating?". Returns `{busy, reason, idle_for_ms}`. No traffic to Maia. |
| `maia__ping` | *(v4.2.1)* Cheap liveness probe — sends "ping", waits up to `timeout_sec` (default 5s), returns `{alive, latency_ms, response, timed_out}`. |
| `maia__health` | *(v4.2.1)* Bridge state without traffic. Per-transport availability + last latency, in-flight handle bindings, embedded busy snapshot. |
| `maia__new_chat` | *(v4.2.1)* Programmatic click of Maia's "New chat" button — wipes Maia's panel context. Always preceded by `maia__busy`. |
| `maia__force_tier` | Override the transport tier (debug aid) |

Two-tier transport: injected JS agent (Tier 1, fast) with DOM-scrape fallback (Tier 2). Both use Studio Pro's `--remote-debugging-port`; the Maia panel must be visible while these tools are in use.

Enable both families in **Settings → Concord MCP** (sub-toggles for each).

> **macOS:** the four hotkey-based UI-action tools work on Mac via `osascript` driving System Events to keystroke Studio Pro (identified by Unix PID, so the `.app` display name doesn't matter). One-time setup: macOS prompts for Accessibility permission the first time Concord MCP runs — open **System Settings → Privacy & Security → Accessibility** and enable Studio Pro. Until you grant it, the calls fail with a clear "Accessibility permission not granted" message that Claude can relay to you. The two service-based tools (`get_active_run_configuration`, `get_app_status`) work on both platforms with no permissions needed. **Maia integration is Windows-only**; the toggle appears disabled on Mac and the `maia__*` tool family is omitted from the Concord MCP advertise list when running on macOS — see [docs/MAIA_MAC_FEASIBILITY.md](./docs/MAIA_MAC_FEASIBILITY.md) for why (WKWebView vs. WebView2, no host opt-in, no Mendix Extensions API surface for Maia today).

### Settings panel

Left-rail navigator (the Microsoft Teams pattern, not Studio Pro's deep tree). Six sections:

1. **General** — tab persistence, ring-buffer KB, scrollback lines.
2. **Shell** — shell selector (auto-detected list), launch arguments.
3. **Studio Pro MCP** — enable + per-CLI client list (Claude Code, Copilot CLI, Codex).
4. **Concord MCP** — enable Concord's in-process MCP server. Two sub-toggles: **Studio Pro UI actions** (run / stop / refresh / save / status tool family) and **Maia integration** (Maia tool family, Windows-only — disabled on macOS). Refresh-from-disk hotkey is configurable here.
5. **Skills** — install bundled Mendix skill packs into the open project. Master toggle + per-CLI checkboxes (Claude Code → `.claude/skills/`, Copilot CLI → `.github/skills/`, Codex → `.codex/skills/`). Each Save refreshes the bundled folders so a Concord upgrade ships new skills automatically; user-authored skills sitting alongside in the same directory are left intact.
6. **About** — version, log file path, settings file path, the CoE Team logo (hover to spin).

Modal title: "Concord Settings". Footer credit on every section: "Built by the Siemens CoE Team."

---

## Bundled skill packs (the 7 that install)

Each is a single `SKILL.md` file with YAML frontmatter that the CLI agent auto-discovers when running inside the project.

| Skill | What it teaches the CLI |
|---|---|
| `mendix-microflow-common` | Core invariants for safe microflow editing — re-read after mutate, edge spacing, batching rules |
| `mendix-microflow-syntax` | The exact JSON shape Studio Pro's MCP expects for microflow activities, splits, loops, end events |
| `mendix-microflow-update` | Step-by-step recipes for replace, insert, remove operations on existing microflows |
| `mendix-page-gen` | Page templates, design-properties bindings, snippet doctrine — what to use, what to never inline |
| `mendix-view-entities` | View-entity patterns + the OQL idioms Studio Pro's MCP supports |
| `mendix-workflow-common` | Core invariants for workflow editing |
| `mendix-workflow-update` | Recipes for safe workflow mutations |

Per-CLI install paths: Claude Code → `.claude/skills/`; Copilot CLI → `.github/skills/`; Codex → `.codex/skills/` (opt-in).

### Always-loaded build rules (Claude Code only, for now)

Alongside the skill packs, Concord installs a project-level rules document for Claude Code:

- **`<project>/.claude/rules/concord-build-rules.md`** — ~360 lines, 15 sections. Governs *how* Claude Code works inside this Mendix project: tool hierarchy (Studio Pro MCP / Concord MCP / Maia / web search / docs.mendix.com — and what's forbidden), page-via-Maia doctrine, persistence + recovery ladders for MCP errors, the named failure modes to guard against (orphan pages, shell microflows, ActionButton wiring trap, letter-not-spirit compliance, end-of-build punt-lists), Studio Pro UI handoff catalog (layouts, Navigation document, Mark-as-UI-resources, etc.), new-project-equals-new-module rule, layout-first for branded apps, sibling-theme-module + Atlas pattern, three-part verification gate, plan-before-write, persisting learnings during a build.
- **`<project>/.claude/rules/project/`** — your space for project-specific rules (domain glossary, design-system tokens, integration patterns). Pre-created on first install with a README stub; never overwritten thereafter. Drop any `.md` files in here and they auto-import into Claude Code on the next session start.
- **`<project>/CLAUDE.md`** — created or updated with a fenced `<!-- BEGIN CONCORD MANAGED -->` block that `@`-imports the rules file plus every `.md` in `.claude/rules/project/`. Anything you write outside the fence is preserved across Saves and across Concord upgrades.

The rules refresh on every Save (so a Concord upgrade ships rule changes automatically). Top-level rule files prefixed `concord-` are Concord-managed; user-authored siblings without that prefix are left untouched.

**v4.2.1:** all three CLIs are lit up. Each ticked CLI gets the same `concord-*.md` rules content + a managed-block import file: Claude → `.claude/rules/` + `CLAUDE.md`, Codex → `.codex/rules/` + `AGENTS.md`, Copilot CLI → `.github/rules/` + `.github/copilot-instructions.md`. Same fenced-block markers, same orphan-cleanup, same atomic write semantics. The base manager class handles all three; per-CLI subclasses set only the destination file.

### macOS skill variant — `mendix-page-gen`

The Windows version of `mendix-page-gen` instructs the CLI agent to delegate page writes to Maia via the Concord MCP `maia__ask` tool (Studio Pro's MCP doesn't expose `pg_*` tools, so Maia is the only path). On macOS, those tools aren't available because Maia integration is Windows-only.

To work around this, Concord ships a **Mac-specific variant of `mendix-page-gen`** at [`skills-mac/mendix-page-gen/SKILL.md`](./skills-mac/mendix-page-gen/SKILL.md). On macOS, the Skill installer overlays this file on top of the Windows version at install time, so what lands in `<project>/.claude/skills/mendix-page-gen/SKILL.md` is the Mac variant. The widget catalog and rules are identical; only the head section changes — instead of "delegate to Maia", the Mac variant tells the CLI to:

1. Build the `pg_write_page` JSON locally (same recipe).
2. **Print a copy-paste prompt to the user** with explicit instructions: "Open Maia in Studio Pro, paste this prompt, send, reply `done` when Maia finishes."
3. **Stop and wait** for the user to confirm.
4. Verify directly with `ped_check_errors` (CLI-side, not user-reported).

The other 6 skill packs (`mendix-microflow-*`, `mendix-view-entities`, `mendix-workflow-*`) don't reference Maia and are identical on both platforms — no Mac variant needed.

---

## Keyboard shortcuts

| Shortcut | What it does | Configurable? |
|---|---|---|
| `F5` | Run the local Mendix runtime (Studio Pro hotkey, mirrored by `concord-mcp`'s `run_app` tool) | No (Studio Pro fixed) |
| `Shift+F5` | Stop the runtime (mirrored by `stop_app`) | No |
| `F4` | Refresh project from disk (mirrored by `refresh_project`) | **Yes** — Settings → Concord MCP → Refresh-from-disk hotkey |
| `Ctrl+S` | Save all (mirrored by `save_all`; best-effort — Studio Pro routes the keystroke to whichever child window has focus) | No |

The hotkey-driven tools work on macOS too (via `osascript` driving System Events), provided you've granted Studio Pro Accessibility permission once.

---

## Upgrades

Concord tracks the version that last applied wiring defaults to a project (`lastAppliedVersion` in `<project>/resources/terminal-settings.json`).

- **First open of a fresh Mendix project (no Concord settings file).** Concord writes `.mcp.json` for Claude Code + Copilot CLI, installs the bundled skill packs into `.claude/skills/` and `.github/skills/`, persists the settings file, and stamps the current version. Three notice banners explain what just happened.
- **First open after a Concord upgrade (existing settings file, older stamp).** Concord re-applies the wiring keys (MCP enables, sub-toggles, skill clients) to the new defaults so you get any new functionality materialized to disk without manually opening Settings and saving. Runtime preferences (shell, theme, ring buffer, scrollback, restore-tabs, refresh hotkey) are preserved verbatim. One banner: `Updated to {ver}. Rewired: ... Open Settings to adjust.`
- **Already-current stamp.** No-op. Concord doesn't re-apply on every open.
- **Stamp newer than installed Concord.** Also no-op (a colleague pulled a project last edited from a machine running a more recent Concord — Concord never downgrades the wiring).

If your `terminal-settings.json` is corrupt for any reason (manual edit gone wrong, disk corruption), Concord renames it to `terminal-settings.json.broken-{timestamp}.bak` so you can recover your custom shell / theme / scrollback values, then defaults take over.

---

## Privacy / data

Concord is loopback-only. The Concord MCP server binds to `127.0.0.1`. Maia integration uses Studio Pro's local WebView2 `--remote-debugging-port`. Nothing leaves your machine. The bundled skill packs are static `SKILL.md` files copied into your project — no telemetry, no callbacks. Your CLI agents (Claude Code, Codex, Copilot CLI) follow their own privacy contracts; Concord just wires them to local Mendix tooling.

---

## What Concord writes to disk

Every file Concord can touch in a project:

| Path | What | When |
|---|---|---|
| `<project>/.mcp.json` | Claude Code + Copilot CLI MCP config (entries: `mendix-studio-pro`, `concord-mcp`) | When **Studio Pro MCP** or **Concord MCP** is enabled with Claude / Copilot ticked |
| `<project>/.claude/skills/<7 folders>` | Bundled Mendix skill packs (Claude Code path) | When **Skills** master + Claude Code ticked |
| `<project>/.github/skills/<7 folders>` | Same skill packs (Copilot CLI path) | When **Skills** master + Copilot ticked |
| `<project>/.codex/skills/<7 folders>` | Same skill packs (Codex path; **opt-in only**) | When **Skills** master + Codex ticked |
| `<project>/.claude/rules/concord-build-rules.md` | Always-loaded build rules for Claude Code (tool hierarchy, page-via-Maia doctrine, layout-first, theme-module pattern, verification gates) — refreshed every Save | When **Skills** master + Claude Code ticked |
| `<project>/.claude/rules/project/README.md` | Stub README for the user-owned project-specific rules folder | Pre-created **once** on first install; never overwritten thereafter |
| `<project>/CLAUDE.md` | Project-root file with a managed block (`<!-- BEGIN CONCORD MANAGED -->` ... `<!-- END CONCORD MANAGED -->`) that `@`-imports the rules file plus every `.md` in `.claude/rules/project/`. Content outside the fence is preserved verbatim | On every Save when Claude rules are installed |
| `<project>/resources/terminal-settings.json` | Concord's own settings (theme, shell, MCP toggles, version stamp) | On Save |
| `<project>/resources/terminal-state.json` | Tab-restore state (which shells were open) | On every tab open / close / exit |
| `<project>/resources/terminal.log` | Diagnostic log (INFO / WARN / ERROR, per-line append) | Continuously |
| `<project>/resources/terminal-settings.json.broken-{ts}.bak` | Backup of a settings file that failed to parse | Only when the file is corrupt at Load time |

**Outside the project, user-global writes:**

| Path | What | When |
|---|---|---|
| `~/.codex/config.toml` | Codex MCP config (entries: `mendix-studio-pro`, `concord-mcp`) | Only when **Codex** is ticked under Studio Pro MCP or Concord MCP |
| `~/Library/Application Support/Concord/zsh/.zshrc` (macOS only) | ZDOTDIR override that prepends Homebrew to PATH so `claude`, `codex`, `gh` resolve out of the box | When a `zsh` tab is spawned |

Codex is opt-in only because its config lives in user-global state, not in the project tree. Auto-enabling it would touch state outside the boundary you can git-ignore from a Mendix project.

---

## Logs

`<project>/resources/terminal.log` — thread-safe per-line append log. INFO / WARN / ERROR. Captures extension lifecycle, action server start/stop, MCP probe results, paste diagnostics. Path also visible in **Settings → About → Log file**.

`<project>/resources/terminal-settings.json` — Concord's own settings (path also visible in **Settings → About → Settings file**).

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
# edit MendixDeployTarget10x / MendixDeployTarget11x to your project root(s)

# Build + deploy
dotnet build

# Test
dotnet test
```

The `BuildUi` target runs `npm install` (first build only) + `node esbuild.mjs` to bundle the xterm.js TypeScript into `wwwroot/terminal.bundle.js`. The `DeployToMendix` target then copies build output into each deploy-target project's `extensions/Concord11x/` or `extensions/Concord10x/` directory depending on which per-host property is set. See [DEPLOYING.md](./DEPLOYING.md) for the per-host property details.

---

## Project layout

```
src/Concord.Core/             Shared library, no Studio Pro reference
  Terminal/                   PTY, settings, web server message DTOs, skill installer
  Mcp/                        Concord MCP host (StudioProActions + ActionServer + ToolCatalog)
  Spmcp/                      Source-merged MCPExtension tool catalog (87 tools), routes through HostServices
  Maia/                       CDP-based Maia bridge + injected JS agent
  Interop/                    Host service interfaces (registry pattern)
src/Concord.Host11x/          Studio Pro 11.x host
  MenuExtensions/             [Export] menu items
  Pane/                       Dockable pane + view model
  Ui/                         TerminalWebServer (Studio Pro IWebServer adapter)
  Interop/                    11.x service implementations
  Spmcp/                      SpmcpToolBootstrap11x — registers catalog tools at MEF activation
  Host11xEntry.cs             MEF activation entry — initializes HostContext + HostServices
src/Concord.Host10x/          Studio Pro 10.x host
  MenuExtensions/             TerminalMenuExtension — opens the dockable pane
  Pane/                       Dockable pane + view model (ported against 10.21.1 ExtensionsAPI)
  Ui/                         TerminalWebServer
  Interop/                    10.x service implementations
  Spmcp/                      SpmcpToolBootstrap10x — registers catalog tools at MEF activation
  Host10xEntry.cs             MEF activation entry
ui/src/                       TypeScript UI (xterm.js, settings modal, bridge, icons, logo)
ui/src/bridge.ts              WebView2 (Windows) and WKWebView (Mac) transport
ui/index.html                 Single-page UI bundled into the extension
skills/                       7 bundled Mendix skill packs (microflow, page, view-entity, workflow patterns)
skills-mac/                   Mac overlay — mendix-page-gen variant for macOS
rules/                        Always-loaded build rules (concord-build-rules.md)
wwwroot/                      Bundled UI assets (generated by esbuild — not committed)
tests/                        xunit test suite
docs/PASTE.md                 Paste pipeline rationale + diagnostic playbook
docs/superpowers/             Original design docs (specs + plans)
modules/Concord.mxmodule      Studio Pro add-on module wrapper for marketplace distribution
Directory.Build.props.example Per-host deploy target configuration template
Terminal.sln                  Solution file (Concord.Core + Concord.Host11x + Concord.Host10x)
```

---

## License

Apache 2.0 — see [LICENSE](./LICENSE).
