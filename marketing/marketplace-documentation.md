# Concord — Marketplace Documentation copy (paste into the Documentation field)

> Save this file as the canonical source of truth. Paste the content below
> (everything from the horizontal rule on) into the Marketplace
> **Documentation** field. Markdown is rendered. Tone: practical, concise,
> developer-focused. Free structure — does not follow the previous template.

---

# Documentation

Concord is a Mendix Studio Pro extension. It installs as a dockable pane that holds a tabbed terminal, plus an in-process MCP server, plus a set of bundled Mendix skill packs. Together those three pieces make AI agent CLIs (Claude Code, OpenAI Codex, GitHub Copilot CLI) first-class collaborators on your Mendix project.

## Installing

The Marketplace **Use in Studio Pro** button installs Concord into the Mendix project you're currently working on. After install:

1. **Restart Studio Pro.** Loaded extensions don't hot-reload.
2. **Approve the trust prompt.** Studio Pro asks once, the first time Concord wants to load native code (the PTY backend). Approve to enable.
3. **Open the Concord pane.** It appears in the right-pane strip alongside Properties, Toolbox, and Maia. Click the **Concord** tab.

The first time Concord opens against a project that has no Concord settings yet, **it wires itself up automatically:**

- Writes `<project>/.mcp.json` so Claude Code and Copilot CLI both see Studio Pro's MCP server *and* Concord's own MCP server
- Installs the 7 bundled Mendix skill packs into `<project>/.claude/skills/` (Claude Code) and `<project>/.github/skills/` (Copilot CLI)
- Installs the always-loaded build rules into `<project>/.claude/rules/concord-build-rules.md` (Claude Code) and creates a managed `@`-import block in `<project>/CLAUDE.md` so the rules auto-load on every session
- Pre-creates `<project>/.claude/rules/project/` (with a README stub) for your own project-specific rule additions
- Starts the Concord MCP server on `localhost:7783` (or auto-fallback if busy)
- Persists Concord's settings file at `<project>/resources/terminal-settings.json` and stamps the current Concord version into it

You'll see one to three short banners explaining what just happened. After that, click `+` on the tab strip to spawn a tab and run any CLI you have installed.

## What Concord adds

### A real terminal pane

Multi-tab PTY rooted at the open project's directory. PowerShell on Windows, your `$SHELL` (typically `zsh`) on macOS — pick from auto-detected shells in **Settings → Shell**. Tab names follow `Pwsh - 1`, `Bash - 2`, `Cmd - 3` — short, readable, gap-filling. Tabs persist across Studio Pro restarts so an interrupted session reopens where you left it. Theme follows Studio Pro automatically.

### The Concord MCP server

A second MCP server hosted in Concord itself, wire identity `concord-mcp`, listening on `127.0.0.1:7783`. It exposes two tool families:

- **Studio Pro UI actions** — `run_app`, `stop_app`, `refresh_project`, `save_all`, `get_active_run_configuration`, `get_app_status`. Lets the CLI drive the IDE: start the runtime, stop it, refresh the project from disk, save all open documents, ask whether the app is running, what configuration is active. The hotkey-driven tools work on macOS too via Accessibility (one-time permission prompt).
- **Maia integration** *(Windows)* — `maia__send`, `maia__status`, `maia__wait`, `maia__ask`, `maia__reset`, `maia__force_tier`. Programmatic access to Studio Pro's in-IDE AI assistant. The CLI can ask Maia to do something Maia is uniquely good at (page generation, for instance) and wait for the response, all without you context-switching.

Toggle each family independently in **Settings → Concord MCP**.

### Bundled Mendix skill packs

Skills are short Markdown files that CLIs auto-discover and follow. Concord ships seven, designed specifically for driving Mendix through both MCP servers correctly:

| Skill | What it teaches the CLI |
|---|---|
| `mendix-microflow-common` | Core invariants — re-read after mutate, edge spacing, batching rules |
| `mendix-microflow-syntax` | Exact JSON shape Studio Pro's MCP expects for activities, splits, loops |
| `mendix-microflow-update` | Step-by-step recipes for replace, insert, remove on existing microflows |
| `mendix-page-gen` | Page templates, design-properties bindings, snippet doctrine |
| `mendix-view-entities` | View-entity patterns and the OQL idioms Studio Pro's MCP supports |
| `mendix-workflow-common` | Core invariants for workflow editing |
| `mendix-workflow-update` | Recipes for safe workflow mutations |

Concord installs these per-CLI when you tick **Settings → Skills → Claude Code / Copilot CLI / Codex**. Each save refreshes the bundled content; user-authored skills in the same directory are left intact.

### Always-loaded build rules *(Claude Code, new in 4.1.4)*

Alongside the skill packs, Concord installs a project-level rules document that auto-loads into every Claude Code session running inside the project:

- **`<project>/.claude/rules/concord-build-rules.md`** (~360 lines, 15 sections). Governs *how* Claude Code works inside this Mendix project, not *what* to build. Covers the closed tool hierarchy (Studio Pro MCP / Concord MCP / Maia / web search / docs.mendix.com — and what's forbidden), the page-via-Maia doctrine, persistence + recovery ladders for unexpected MCP errors, the named failure modes to guard against (orphan pages, shell microflows, ActionButton wiring trap, letter-not-spirit compliance, end-of-build "manual steps required" punt-lists), the Studio Pro UI handoff catalog, the new-project-equals-new-module rule, layout-first for branded apps, the sibling-theme-module + Atlas pattern, the three-part verification gate, plan-before-write for non-trivial builds, and persisting learnings during a build.
- **`<project>/.claude/rules/project/`** — your space for project-specific rules: domain glossary, design-system tokens, integration patterns, anything you want every Claude Code session in this project to load on startup. Pre-created on first install with a README stub; never overwritten thereafter. Survives every Concord upgrade.
- **`<project>/CLAUDE.md`** — created or refreshed with a fenced `<!-- BEGIN CONCORD MANAGED -->` ... `<!-- END CONCORD MANAGED -->` block that `@`-imports the rules file plus every `.md` in `.claude/rules/project/`. Anything you write outside the fence is preserved verbatim across Saves and Concord upgrades.

The rules file is refreshed on every Save (so future Concord releases ship rule updates automatically). Top-level rule files prefixed `concord-` are Concord-managed; user-authored siblings without that prefix are left untouched.

Phase 1 covers Claude Code only. Codex (`AGENTS.md`) and Copilot CLI (`.github/copilot-instructions.md`) follow the same fenced-block pattern in their respective files; they're wired as no-ops in v4.1.4 and light up in a follow-up phase once the Claude path is proven on more real builds.

### Auto-wired CLI configs

When you enable **Studio Pro MCP** or **Concord MCP** in Settings, Concord writes the connection details into:

- `<project>/.mcp.json` — read by Claude Code and Copilot CLI
- `~/.codex/config.toml` — read by Codex (only if you tick Codex)

Existing entries in those files are preserved; Concord upserts only the entries it owns (`mendix-studio-pro`, `concord-mcp`).

## Working day-to-day

A typical session: open Studio Pro, open the Concord pane, your previous tabs come back. Spawn a new tab, type `claude`, ask it to scaffold a microflow that handles "Approve Loan Request." Claude reads your domain model through Studio Pro's MCP, drafts the microflow, asks you to confirm. You approve. Claude writes the microflow through Studio Pro's MCP, calls Concord MCP's `run_app` to start the runtime, then `get_app_status` to confirm it's up. You alt-tab to the browser, smoke-test, come back, ask Claude for a tweak.

Three windows — IDE, terminal, browser — collapse into two.

Other patterns that work well:

- **Run Codex in tab 2 for a refactor** while Claude Code stays in tab 1 with project context.
- **Long-running build in tab 3.** Tab persistence keeps it alive across Studio Pro restarts.
- **Paste a multi-page chat transcript or 50-line policy doc** straight into Claude Code's prompt. Concord's paste pipeline handles it as one atomic paste with the native `[Pasted text +N lines]` collapse.

## Settings panel — what's where

Open Settings via the gear icon at the top-right of the Concord pane. Six sections:

1. **General** — tab persistence, ring buffer, scrollback lines.
2. **Shell** — default shell each new tab spawns into; auto-detected shell list; per-shell launch arguments.
3. **Studio Pro MCP** — enable + per-CLI client list (Claude Code, Copilot CLI, Codex). Concord auto-probes Studio Pro's actual MCP port from `Settings.sqlite` so you never have to keep ports in sync manually. **Codex is opt-in** because its config lives in user-global state, not in your project tree.
4. **Concord MCP** — enable Concord's in-process MCP server. Two sub-toggles for the tool families above. Refresh-from-disk hotkey configurable here (default F4).
5. **Skills** — install the bundled Mendix skill packs into the open project, per-CLI.
6. **About** — version, log file path, settings file path, the CoE Team logo.

## Updates and upgrades

Concord remembers the version that last applied wiring defaults to your project. On first open after an upgrade:

- **Older stamp** → the wiring defaults are re-applied automatically. New skills install, new MCP entries land, new sub-toggles enable. Your runtime preferences (shell, theme, ring buffer, scrollback, restore-tabs, refresh hotkey) are preserved verbatim. A short banner explains what changed and points you to Settings if you want to adjust anything.
- **Same or newer stamp** → no-op. Concord doesn't re-apply on every open, and never downgrades the wiring of a project a colleague last edited from a newer Concord version.

If your settings file is somehow corrupted (manual edit gone wrong, disk corruption), Concord renames it to `terminal-settings.json.broken-{timestamp}.bak` so you can recover your custom values, and falls back to defaults instead of failing silently.

## macOS notes

Concord runs on macOS 10.15+ on Studio Pro 11.10+. Most of what's in this doc applies identically. Two differences worth knowing:

- **Maia integration is Windows-only.** Driving Maia from outside Studio Pro requires inspecting its WebView; on Windows that's WebView2's Chrome DevTools Protocol; on macOS that's WKWebView, which only allows external inspection if the host app explicitly opts in (Studio Pro doesn't). The full feasibility analysis is in the project's `docs/MAIA_MAC_FEASIBILITY.md`. To work around this, Concord ships a Mac-specific variant of `mendix-page-gen` that prints a copy-paste prompt for *you* to hand to Maia in Studio Pro instead of calling Maia programmatically. Other skills don't reference Maia and are platform-identical.
- **Accessibility permission for hotkey tools.** The four hotkey-driven Studio Pro UI tools (`run_app`, `stop_app`, `refresh_project`, `save_all`) work on Mac via `osascript` driving System Events. macOS prompts for Accessibility permission the first time. Open **System Settings → Privacy & Security → Accessibility**, find Studio Pro, enable. Until you grant it, the calls fail with a clear "Accessibility permission not granted" message that the CLI can relay back to you.

## What Concord writes to your disk

Project-local writes:

- `<project>/.mcp.json` — Claude Code + Copilot CLI MCP config
- `<project>/.claude/skills/<7 folders>` — bundled skills, Claude Code path
- `<project>/.github/skills/<7 folders>` — bundled skills, Copilot CLI path
- `<project>/.codex/skills/<7 folders>` — bundled skills, Codex path *(opt-in)*
- `<project>/.claude/rules/concord-build-rules.md` — always-loaded build rules (Claude Code; refreshed every Save) *(new in 4.1.4)*
- `<project>/.claude/rules/project/README.md` — stub for the user-owned project-rules folder; **pre-created once on first install, never overwritten thereafter** *(new in 4.1.4)*
- `<project>/CLAUDE.md` — managed `<!-- BEGIN CONCORD MANAGED -->` block at project root that `@`-imports the rules; content outside the fence preserved verbatim *(new in 4.1.4)*
- `<project>/resources/terminal-settings.json` — Concord's settings + version stamp
- `<project>/resources/terminal-state.json` — tab-restore state
- `<project>/resources/terminal.log` — diagnostic log (lifecycle, MCP probes, paste diagnostics — never clipboard contents)

User-global writes (only when you opt in):

- `~/.codex/config.toml` — Codex MCP config (only when Codex is ticked)
- *(macOS only)* `~/Library/Application Support/Concord/zsh/.zshrc` — ZDOTDIR override that prepends Homebrew to PATH so `claude`, `codex`, `gh` resolve out of the box

## Privacy

Concord is loopback-only. The Concord MCP server binds to `127.0.0.1`. Maia integration uses Studio Pro's local WebView2 debug port. Nothing leaves your machine through Concord itself. The bundled skill packs are static Markdown files copied into your project — no telemetry, no callbacks, no analytics. The CLI agents you run inside Concord follow their own privacy contracts.

## FAQ

**Does Concord cost anything?** No — Apache 2.0, source on GitHub. The CLI agents you run inside it have their own pricing.

**Will it touch my git history?** Only inside the open Mendix project, and only files you can choose to git-ignore. Concord doesn't run git for you.

**Does it work with my existing `.mcp.json`?** Yes. Concord upserts only its own named entries; everything else in the file is preserved.

**What if I already have my own skills in `.claude/skills/`?** They're left alone. Concord installs and removes only its own bundled skill folders.

**Can a teammate share my Concord settings?** The MCP wiring lives in `<project>/.mcp.json` which is in your project tree, so yes — any teammate who clones the project gets the same wiring when they open it in Studio Pro.

**Will Mendix build something like this natively?** Probably some of it. The agent-augmented IDE is where the platform is heading — and that's a good thing. Concord is the bridge for teams who want first-class agent workflows today; expect Mendix to absorb pieces of this pattern over time. We'll keep Concord open-source and build on whatever Mendix ships.

**Where do I report a bug or ask for a feature?** Open an issue on the GitHub repository. Security issues — please use a private security advisory.

## Built by

The **Siemens CoE Team** — Mendix builders shipping the workflow we wanted on our own projects. Apache 2.0. Source on GitHub. Powered by Mendix's Studio Pro MCP server.
