# Concord — Marketplace Overview copy (paste into the Overview field)

> Save this file as the canonical source of truth. Paste the content below
> (everything from the horizontal rule on) into the Marketplace **Overview**
> field. Markdown is rendered. Tone: helpful to developers, positive about
> Mendix and Siemens, not overly complicated. Free structure — does not
> follow the previous template.

---

## Concord brings AI agent CLIs into Studio Pro

Concord installs as a dockable pane inside Mendix Studio Pro 11.10+. Inside that pane is a real terminal. Inside that terminal you run **Claude Code**, **OpenAI Codex**, or **GitHub Copilot CLI** — and those CLIs talk to your Mendix project through Studio Pro's MCP server and through Concord's own MCP server. No window-switching, no config-file plumbing, no yak-shaving to set up.

Mendix's recent investment in **Studio Pro's MCP server** unlocked something genuinely new — a structured, programmatic surface that AI agents can read and write through. Concord is the workspace built around that opportunity. We add the second piece — the embedded terminal where the agents actually live — and we wire the integration plumbing so it just works on first open.

## Why you'd want this

**You can build Mendix apps with AI agent CLIs as full collaborators.** Not "ask AI to draft a bit of code outside the IDE" — actual collaborators who can read your domain model, edit your microflows, generate pages, run the app, watch the run state, and hand work back to you in the same window where Studio Pro lives.

**Setup is automatic on the first open.** Drop Concord into your Mendix project, restart Studio Pro, open the Concord pane. The MCP wiring lands on disk for Claude Code and Copilot CLI, the bundled Mendix skill packs install themselves, and the Concord MCP server starts listening — all before you click anything.

**The terminal itself is good.** Multi-tab PTY (PowerShell, bash, cmd, zsh — all auto-detected). Theme follows Studio Pro automatically. Tabs survive Studio Pro restarts. Multi-line paste actually works (50 lines paste as 50 lines, not 50 separate prompt submissions). xterm.js with Cascadia Mono, the works.

## What ships in the extension

| What | Where |
|---|---|
| **Concord pane + tabbed PTY terminal** | Dockable pane in Studio Pro's right rail |
| **Concord MCP server** | In-process, on `localhost:7783` (auto-fallback if busy) |
| **Studio Pro UI tools** | 6 MCP tools — `run_app`, `stop_app`, `refresh_project`, `save_all`, `get_active_run_configuration`, `get_app_status` — so the CLI can drive the IDE itself, not just read the model |
| **Maia integration** *(Windows)* | 10 MCP tools — `maia__send`, `maia__status`, `maia__wait`, `maia__ask`, `maia__reset`, `maia__force_tier`, plus the v4.2.1 introspection toolkit (`maia__busy`, `maia__ping`, `maia__health`, `maia__new_chat`) — programmatic access to Studio Pro's in-IDE AI assistant, including read-only "is Maia generating?" probes and bridge-state diagnostics without burning a real call |
| **7 bundled Mendix skill packs** | Installed into your project's `.claude/skills/`, `.github/skills/`, and `.codex/skills/` (each ticked CLI) — prescriptive playbooks that teach the CLIs how to drive Mendix correctly |
| **Always-loaded build rules** *(all three CLIs as of v4.2.1)* | A project-level `concord-*.md` set that auto-loads into every Claude Code, Codex, and Copilot CLI session via a managed block in `CLAUDE.md`, `AGENTS.md`, and `.github/copilot-instructions.md` respectively. Governs *how* the agent works — tool hierarchy, page-via-Maia doctrine, task-scoped failure cap, errors-before-`run_app` gate, Maia-as-page-fixer tiebreaker, layout-first for branded apps, sibling-theme-module pattern, seed-data self-service-button pattern, three-part verification gate. Plus a `rules/project/` folder per CLI for your own project-specific additions that survives Concord upgrades |
| **Auto-wired CLI configs** | `.mcp.json` (Claude Code + Copilot CLI) and `~/.codex/config.toml` (Codex) point at both MCP servers — all three CLIs default-on as of v4.2.1, with a first-run banner explaining the user-global Codex config write. **v4.2.2:** Codex `config.toml` hygiene is automatic — orphan `tools.*` sub-sections from pre-v1.3.0 migrations are cleaned up on Save, and Codex's "External agent config detected" prompt is suppressed for Concord-managed projects so it stops asking to migrate Claude config you already have configured |

## How a session looks

1. Open your Mendix project in Studio Pro 11.10+.
2. Click the **Concord** tab in the right pane strip.
3. New tab → `claude` (or `codex`, or `gh copilot`).
4. Ask it anything about your project. It reads through Studio Pro's MCP, writes through Concord's MCP, drives the IDE through the UI-action tools, and hands you back to verify.
5. F5 still runs your app. Maia is still right there. Concord just makes the agent a peer.

## Built for Mendix, by Mendix builders

Concord is built and maintained by the **Siemens CoE Team** — Mendix builders ourselves, shipping the workflow we wanted on our own projects. Apache 2.0, source on GitHub, free to use. The CLIs you run inside Concord (Claude Code, Codex, Copilot CLI) follow their own pricing.

## Compatibility

- **Studio Pro:** 11.10.0 or newer
- **Operating system:** Windows 10 build 17763 (1809) or newer · macOS 10.15 (Catalina) or newer
- **.NET runtime:** 8.0 (bundled with Studio Pro 11)
- **Optional:** any combination of Claude Code, OpenAI Codex, GitHub Copilot CLI installed on your machine
