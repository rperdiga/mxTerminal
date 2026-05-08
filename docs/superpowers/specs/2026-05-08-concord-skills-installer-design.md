# Concord Skills installer — bundled Mendix skill packs for Claude Code, Codex, and Copilot CLI

**Date:** 2026-05-08
**Status:** Design — approved, pending implementation plan
**Audience:** Concord maintainers
**Scope:** Replaces the "Coming soon" placeholder in the Skills section of the Concord settings modal ([ui/index.html:852-861](../../../ui/index.html#L852-L861)).

## Context

Concord already wires Studio Pro's MCP server (`mendix-studio-pro`) and Concord's own action-bridge MCP server (`concord-mcp`) into the three CLIs Concord supports inside its terminal pane: Claude Code, GitHub Copilot CLI, and Codex. The MCP wiring lives in [src/McpJsonConfigurator.cs](../../../src/McpJsonConfigurator.cs) (project-local `.mcp.json`, shared by Claude Code and Copilot CLI) and [src/McpTomlConfigurator.cs](../../../src/McpTomlConfigurator.cs) (user-global `~/.codex/config.toml`).

A curated set of 7 Mendix skill packs already exists at [`C:\Projects\AltairTraversalViewer\.claude\skills\`](file:///C:/Projects/AltairTraversalViewer/.claude/skills/) — folders shaped `<name>/SKILL.md` with YAML frontmatter (`name:` + `description:`) — and demonstrably make Claude Code more competent at driving Studio Pro's MCP and Concord MCP. Today they are hand-managed and travel with one project. Other Mendix projects don't get them unless the developer copies the folder manually.

The Skills section of the Concord settings modal is currently an aspirational placeholder. This design fills it in.

## Goals

1. Concord ships a bundled set of Mendix skills inside the extension distribution. Users get them automatically with every Concord deployment; new skills ride the next Concord release the same way the rest of the extension does.
2. When Skills are enabled, Concord installs the bundled skill folders into the open Mendix project's tree, in the location each selected CLI auto-discovers. Per-CLI checkboxes mirror the MCP wiring pattern.
3. Diff/apply lifecycle: enabling a CLI installs; disabling removes only Concord-bundled skill folders, leaving any user-authored skills the developer has dropped alongside them untouched.
4. Save-time UX matches the existing MCP banner + tab-recycle flow so the next CLI prompt sees the new skills with zero manual reload.

## Non-goals

- Per-skill toggles. The 7 skills are a coherent Mendix pack; users either want it or they don't. Per-skill granularity can be added later if asked for.
- Custom skill folders or user-supplied skills. Bundled-only. (User-authored skills already work today by hand-dropping a folder into `.claude/skills/`; Concord doesn't need to manage them.)
- User-scope installs (`%USERPROFILE%\.claude\skills\`, `~/.codex/skills/`). Project-local only — mirrors the project-scope `.mcp.json` strategy and keeps cleanup contained.
- A skill registry / marketplace. The bundle is whatever Concord ships in this version.
- Versioning, drift detection, or "your skill is older than the bundled one" warnings. Each Save overwrites the bundled skill folders so a Concord upgrade refreshes them; users who hand-edited a Concord-bundled skill folder will lose those edits, by design.
- Hot-reload during a session. The recycle-tabs flow that already runs after MCP saves handles this — the new prompt picks up the new files.

## Approach

**Mirror the MCP wiring.** Treat the bundled skill pack the way Concord already treats the Studio Pro MCP server and the Concord MCP server: a single piece of state (`SkillsEnabled` master toggle + `SkillClients` list) that drives an installer/uninstaller per selected CLI on Save, with prev/next-diff semantics.

Considered alternatives:

- **Single shared `.agents/skills/` directory.** Per the discovery research, only Copilot CLI explicitly accepts `.agents/skills/`; Claude Code looks at `.claude/skills/` and Codex at `.codex/skills/`. Sharing one directory would still need per-CLI fallbacks, costing the complexity of approach A with none of the predictability.
- **Symlink one source dir into each CLI's expected location.** Windows symlinks need admin or Developer Mode; Concord runs unelevated inside Studio Pro. Non-starter on Windows.

## Architecture

### Bundling

The extension distribution gains a new top-level folder alongside `wwwroot/`:

```
extensions/Concord/
├── Concord.dll
├── manifest.json
├── wwwroot/
│   ├── index.html
│   └── terminal.bundle.js
└── skills/                              ← new
    ├── mendix-microflow-update/
    │   └── SKILL.md
    ├── mendix-microflow-syntax/
    │   └── SKILL.md
    ├── mendix-microflow-common/
    │   └── SKILL.md
    ├── mendix-page-gen/
    │   └── SKILL.md
    ├── mendix-view-entities/
    │   └── SKILL.md
    ├── mendix-workflow-update/
    │   └── SKILL.md
    └── mendix-workflow-common/
        └── SKILL.md
```

The 7 skill folders are checked into the Concord repo (suggested location: `skills/` at the repo root, mirroring `wwwroot/`). The csproj copies them to the build output and `DeployToMendix` xcopies them into each `MendixDeployTarget`'s `extensions/Concord/skills/` directory, exactly the same way `wwwroot/` is handled today.

Bundled skills are sourced once from `C:\Projects\AltairTraversalViewer\.claude\skills\` and committed to the Concord repo as the canonical source. After this initial copy, edits happen in the Concord repo — the AltairTraversalViewer copy is no longer authoritative.

Runtime resolution uses `IExtensionFileService.ResolvePath("skills")`, the same MEF-injected service `TerminalWebServer` already uses for `wwwroot`.

### `SkillInstaller` (new — `src/SkillInstaller.cs`)

Shape mirrors the existing MCP configurators:

```csharp
public sealed class SkillInstaller
{
    public SkillInstaller(string projectDir, string bundledSkillsRoot, Logger log);

    /// <summary>
    /// Copy every bundled skill folder into <paramref name="targetSubdir"/>
    /// under <c>projectDir</c>. Overwrites existing files in matching skill
    /// folders so a Concord upgrade refreshes the content. Idempotent.
    /// </summary>
    public void InstallAll(string targetSubdir);

    /// <summary>
    /// Remove only the skill folders whose names match a bundled skill.
    /// User-authored sibling folders are left intact. If the parent
    /// <paramref name="targetSubdir"/> is empty after cleanup, remove it
    /// (and its <c>.claude</c> / <c>.codex</c> / <c>.github</c> ancestor
    /// only when those ancestors are themselves empty — never delete a
    /// user's <c>.github/</c> directory).
    /// </summary>
    public void RemoveAll(string targetSubdir);
}
```

`targetSubdir` is one of:
- `".claude/skills"` for Claude Code
- `".codex/skills"` for Codex
- `".github/skills"` for GitHub Copilot CLI

Path separators are normalized via `Path.Combine` so the same constants work on Windows and macOS.

### Settings (additions to `TerminalSettings.cs`)

Two new fields on the `TerminalSettings` record:

```csharp
public sealed record TerminalSettings(
    // ...existing fields...
    bool SkillsEnabled,
    string[] SkillClients);
```

Defaults:
- `SkillsEnabled = false` (off on first upgrade so existing projects don't get unexpected file writes; user opts in explicitly).
- `SkillClients = []` (no CLIs selected by default).

Migration: when loading a settings file written by Concord ≤ 1.3.x (no `skillsEnabled`/`skillClients` keys), both fields fall back to the defaults — same null-coalescing pattern the existing `Load` already uses.

`SkillClients` reuses the same `"claude" | "copilot" | "codex"` string vocabulary as `McpClients`. Independent from `McpClients` because users may want skills installed for one CLI but not MCP, or vice versa.

### Save flow (additions to `TerminalPaneViewModel.HandleSaveSettings`)

A new branch parallel to `ApplyMcpConfig` and `ApplyActionsMcpConfig`:

```csharp
var touchedSkills = ApplySkillsConfig(dir, current, updated);
// ...
var allTouched = touchedPrimary.Concat(touchedActions).Concat(touchedSkills).ToArray();
```

`ApplySkillsConfig` does the same prev/next diff:

```csharp
private string[] ApplySkillsConfig(string projectDir, TerminalSettings prev, TerminalSettings next)
{
    var prevClients = prev.SkillsEnabled
        ? new HashSet<string>(prev.SkillClients, StringComparer.OrdinalIgnoreCase)
        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var nextClients = next.SkillsEnabled
        ? new HashSet<string>(next.SkillClients, StringComparer.OrdinalIgnoreCase)
        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // For each CLI: install if just-added, remove if just-removed.
    // Per-CLI: claude → ".claude/skills", copilot → ".github/skills",
    //          codex  → ".codex/skills"
    // ...
}
```

The bundled-skills root is resolved once via the `IExtensionFileService` instance MEF already provides to `TerminalPaneExtension`, then passed into the installer.

The save-time recycle-tabs path that runs today after MCP touches anything stays the same — `touchedSkills` joins the same `allTouched` array, so when skills change we recycle terminal tabs alongside MCP changes (and any new prompt the user opens after picks up the freshly installed skills).

### UI (additions to [ui/index.html](../../../ui/index.html) and [ui/src/settings-modal.ts](../../../ui/src/settings-modal.ts))

The Skills section replaces the "Coming soon" card with the same shape as the MCP sections:

```html
<section class="settings-section" data-section="skills" role="tabpanel">
  <h4>Skills</h4>
  <p class="section-desc">
    Mendix skill packs are prescriptive playbooks Concord writes into your project so
    the CLIs above know how to drive Studio Pro's MCP and Concord MCP correctly.
  </p>

  <div class="checkbox-row">
    <input id="set-skills-enabled" type="checkbox">
    <label for="set-skills-enabled" style="margin:0">Enable Mendix skill pack</label>
  </div>

  <h5>Bundled in this Concord</h5>
  <ul id="bundled-skills-list" class="bundled-skills-list">
    <!-- populated from settings payload at open time -->
  </ul>

  <h5>CLIs to install for</h5>
  <div class="checkbox-row">
    <input id="set-skills-claude" type="checkbox">
    <label for="set-skills-claude" style="margin:0">
      Claude Code
      <span class="muted">— writes <code>&lt;project&gt;/.claude/skills/</code></span>
    </label>
  </div>
  <div class="checkbox-row">
    <input id="set-skills-copilot" type="checkbox">
    <label for="set-skills-copilot" style="margin:0">
      GitHub Copilot CLI
      <span class="muted">— writes <code>&lt;project&gt;/.github/skills/</code></span>
    </label>
  </div>
  <div class="checkbox-row">
    <input id="set-skills-codex" type="checkbox">
    <label for="set-skills-codex" style="margin:0">
      Codex
      <span class="muted">— writes <code>&lt;project&gt;/.codex/skills/</code></span>
    </label>
  </div>
</section>
```

`SettingsPayload` (TS interface) and the C# `SettingsPayload` record gain:

```ts
interface SettingsPayload {
  // ...existing fields...
  skillsEnabled: boolean;
  skillClients: string[];
  bundledSkills: BundledSkill[];
}

interface BundledSkill {
  name: string;          // folder name, e.g. "mendix-microflow-update"
  description: string;   // pulled from SKILL.md frontmatter
}
```

The `populate()` and `save()` methods in `SettingsModal` mirror the existing MCP per-CLI pattern: a master toggle that disables/enables children, three per-CLI checkboxes, and a save payload that stuffs the picked CLIs into `skillClients`.

The bundled-skills list is populated server-side: on `openSettings`, `BuildSettingsPayload` enumerates the bundled skills directory once and parses each SKILL.md's frontmatter to extract `name` and `description`. (No filesystem access from the WebView; the JS side just renders.)

### Result banner

`HandleSaveSettings` already builds a single `mcpResult` notice listing every touched target. Skills install/uninstall reuses that pipe — typical messages:

- `"Mendix skill pack installed for Claude Code. Restarting open terminals…"`
- `"Mendix skill pack disabled (cleaned up: Claude Code, Codex). Restarting open terminals…"`
- Combined with MCP changes when both happen in one save: `"MCP servers + skills updated for Claude Code. Restarting open terminals…"`

The `BuildResultMessage` helper grows a third arm to cover skill-only changes. The recycle-tabs flow stays the same.

## Tests

- `SkillInstallerTests` — Install all into a temp dir / Remove all leaves user-authored sibling folder intact / Install is idempotent / Install overwrites changed bundled content / Remove cleans up empty parent (`.claude/skills` then `.claude` if empty) but doesn't touch a `.github/` dir that has other content.
- `TerminalSettingsTests` — Round-trip the two new fields with defaults / Migration from a settings file missing both keys defaults `SkillsEnabled = false` and `SkillClients = []`.
- `SettingsModal` (TS) — `bundledSkills` list renders in section order / per-CLI checkboxes are disabled when the master toggle is off / save payload includes `skillsEnabled` and `skillClients` correctly.

Manual end-to-end verification (per [README § Development](../../../README.md#development)):

1. Build + deploy Concord with the skills folder.
2. Open the modal, enable Skills + Claude Code, save.
3. Confirm `<project>/.claude/skills/mendix-microflow-update/SKILL.md` exists with the expected content.
4. Disable Claude Code, save. Confirm the folder is gone.
5. Hand-create `<project>/.claude/skills/my-thing/SKILL.md`. Re-enable, save, disable. Confirm `my-thing/` is intact and only Concord-bundled folders are removed.

## Risks & open questions

- **Codex and Copilot CLI skill conventions are newer than Claude Code's.** Research cited current docs (developers.openai.com/codex/skills, docs.github.com/copilot CLI add-skills) but the conventions are evolving. If a CLI doesn't auto-discover the location we wrote to, the user sees a working file write but a non-functional skill. Implementation plan should include a manual smoke-test against the latest Codex and Copilot CLI versions before release.
- **Refresh-on-Concord-upgrade overwrites user edits to bundled skill folders.** This is intentional (bundled skills are version-locked to Concord), but the README/CHANGELOG note for the release should call it out so users who customize bundled skills aren't surprised.
- **No rollback on partial save failure.** If `.claude/skills/` writes succeed but `.codex/skills/` fails midway, the user ends up with a half-installed state and a one-line error in the result banner. Mirrors how MCP saves behave today (per-target try/catch with collected logs); same bar is fine for v1.
- **`.github/skills/` path for Copilot is the documented location but we should verify Copilot also auto-discovers from `.github/skills/` and not only from a custom-instructions file.** If Copilot CLI requires a different convention at implementation time, swap the `targetSubdir` constant — the rest of the design is unaffected.
