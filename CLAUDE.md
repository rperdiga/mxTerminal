# Concord — project context for Claude Code sessions

> **What this file is for:** orient any Claude Code session that opens in this repo. Read first; the rich details live in the linked files.

## What this repo is

**Concord** — a Mendix Studio Pro 11.10+ extension that embeds a tabbed terminal as a dockable pane. Inside that terminal: Claude Code, OpenAI Codex, or GitHub Copilot CLI, wired through MCP to Studio Pro's MCP server + Concord's own in-process MCP server (Studio Pro UI actions + Maia integration). C# / .NET 8 / Eto.Forms / hand-rolled ConPTY for terminal backend / WebView2 for UI / CDP injection for Maia bridge.

The GitHub repo retains its legacy name `mxTerminal` for URL stability: https://github.com/rperdiga/mxTerminal. The C# assembly name and the user-visible product are `Concord`. **Don't be confused if you see both names.**

## FIRST in any session — read these in order

1. **`~/.claude/projects/c--Workspace-Dev-Projects-mxTerminal/memory/_HANDOFF.md`** — current state, what just shipped, what's next. **Source of truth for "where are we".**
2. **`~/.claude/projects/c--Workspace-Dev-Projects-mxTerminal/memory/MEMORY.md`** — index of all auto-memory files. Topical entry points.
3. **`CHANGELOG.md`** in this repo root — full version history with empirical baselines.

If the user has any non-trivial ask: read whichever specific memory files MEMORY.md indexes for the topic before answering.

## How to do a release — `reference_concord_release_playbook.md`

The complete operational flow from "I changed some code" to "marketplace listing live" is documented in:

`~/.claude/projects/c--Workspace-Dev-Projects-mxTerminal/memory/reference_concord_release_playbook.md`

That file is the SOURCE OF TRUTH for branching conventions, atomic-commit phasing, test discipline, marketing-doc surfaces (there are FOUR — keep MD and HTML in sync), the adversarial-review checkpoint, .mxmodule build (manual Studio Pro step, Neo only), GitHub PR/tag/release flow, and marketplace upload steps. Read it before running release steps from memory — there are gotchas the auto-handoffs don't preserve.

Companion: `reference_concord_mxmodule_build.md` — the click-by-click for the Studio Pro UI step that builds the .mxmodule. Only the user can run those clicks.

## Key paths (this machine)

| Purpose | Path |
|---|---|
| Source repo (active) | `C:\Workspace\Dev\Projects\Concord` |
| Source repo (legacy name, sometimes the cwd Claude launches into) | `C:\Workspace\Dev\Projects\mxTerminal` — same machine, different historical name |
| ConcordPublisher wrapper app (.mxmodule built here) | `C:\Workspace\MendixApps\ConcordPublisher` |
| Testbed (dev builds auto-deploy here too) | `C:\Workspace\MendixApps\TestOSApp3` |
| `.mxmodule` output (gitignored) | `<repo>/modules/Concord.mxmodule` |

`dotnet build` from this repo auto-deploys to BOTH ConcordPublisher AND TestOSApp3 via the `DeployToMendix` MSBuild target (see `Terminal.csproj`). No manual copy needed.

## Architecture cheat sheet (so you know what's where)

- `src/Maia/` — Maia bridge (CDP-injected agent, transport router, introspection toolkit `maia__{busy,ping,health,new_chat}`)
- `src/StudioProActionServer.cs` — in-process HTTP MCP server (`concord-mcp` on port 7783)
- `src/StudioProActions.cs` — UI-action tools (run_app, save_all, etc.)
- `src/TerminalPaneExtension.cs` — MEF entry point + lifecycle + first-run / upgrade-apply
- `src/TerminalPaneViewModel.cs` — WebView ↔ session-manager bridge; settings modal
- `src/TerminalSessionManager.cs` — PTY lifecycle + action-server hosting
- `src/PtySession.cs` + `UnixPtySession.cs` — ConPTY (Windows) / POSIX PTY (Mac)
- `src/{Claude,Agents,CopilotInstructions}MdManager.cs` — manages the per-CLI managed `@`-import block in `CLAUDE.md` / `AGENTS.md` / `.github/copilot-instructions.md`
- `src/RulesInstaller.cs` — installs `concord-*.md` rules into the project's per-CLI `<rules-folder>/`
- `src/SkillInstaller.cs` — installs the 7 bundled Mendix skill packs per-CLI
- `src/SettingsApplyHelper.cs` — central diff-apply for MCP configs, skills, rules across all 3 CLIs
- `src/Mcp{Json,Toml}Configurator.cs` — writes `.mcp.json` (Claude + Copilot) and `~/.codex/config.toml` (Codex)
- `rules/concord-{build-rules,pages-and-themes,model-discipline}.md` — the 3 always-loaded build rules files Concord installs into every Mendix project that opts in
- `skills/` — 7 bundled Mendix skill packs (microflow, page-gen, view-entities, workflow patterns)
- `marketing/` — paste-ready marketplace content (4 surfaces; keep MD + HTML in sync)
- `tests/` — xUnit + FluentAssertions; 241+ tests as of v4.2.2

## Things that bit us before — don't repeat

- **Always update the HTML AND MD versions of marketing docs together.** v4.2.1 cycle missed `marketplace-overview.html`; Neo caught it post-hoc.
- **`gh release create --notes "<inline>"` mishandles heredocs with backticks.** Write notes to a temp file, use `--notes-file`. Clean up after.
- **`Component Type` on the marketplace is IMMUTABLE post-publish.** Always `Module`.
- **`.mxmodule` build requires a Studio Pro UI step** Neo runs manually. Cannot be automated as of Studio Pro 11.10+.
- **Version bump alone is NOT enough** — Studio Pro re-bakes the version into the .mxmodule at export time. Must redo the UI export step.
- **Section disambiguation in TOML editing:** `[mcp_servers.X.]` vs `[mcp_servers.X-actions.]` — the trailing dot in the prefix match is what disambiguates. Test for it explicitly. (See `Remove_PrimaryDoesNotStripActionsChildren_NameDisambiguation` in tests for the regression case.)
- **`gh repo edit` returns 404** on this repo — Neo's account has push but not admin. Don't try to change repo description / topics / social preview.

## Working style — what Neo expects

- **Empirical over theoretical.** Every release ships with empirical baselines — concrete observations from real builds, not speculation. Match that pattern.
- **Fresh-reviewer before merging substantive work.** Adversarial pass on the diff; address NITs in-branch; defer FLAGs to next cycle with a memory capture.
- **Atomic commits per phase.** v4.2.0 was 7 phases; v4.2.1 was 6; v4.2.2 was 3. Phase = natural break point, not arbitrary.
- **Memory promotion path.** Working notes go in `project_concord_v<X.Y.Z>_capture.md`; on ship, promote to PROMOTED status + roll deferred items into the next release's `_HANDOFF.md` backlog. Don't delete history; archive it.
- **Don't guess. Don't fake. Don't break.** The repo's own north star.

## Current state at a glance

Check `_HANDOFF.md` for the live answer. As of last memory write, **v4.2.2 is the latest GitHub release** (https://github.com/rperdiga/mxTerminal/releases/tag/v4.2.2); v4.2.1 is what's live on the marketplace; v4.2.2 awaits Neo's marketplace upload at his discretion.

Next cycle backlog seeded in `_HANDOFF.md`.
