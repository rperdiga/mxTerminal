# Concord — default-on settings + first-run auto-apply

**Date:** 2026-05-08
**Status:** Design — approved, pending implementation plan
**Audience:** Concord maintainers
**Scope:** Replaces `TerminalSettings.Defaults()`'s all-off baseline with an all-on baseline (Codex excluded), and adds a first-run path in `TerminalPaneExtension.Open()` that applies that baseline to disk so a customer who deploys Concord into a new Mendix project gets MCP wiring, Concord MCP, and skill packs functional with zero clicks. v4.1.0 candidate.

## Context

Concord 4.0.0 ships with everything off by default. A customer who installs the extension into a Mendix project sees:
- Studio Pro MCP toggle: off (no `.mcp.json` written).
- Concord MCP toggle: off (no in-process server running).
- Skills: off (no skill packs installed).

To make Concord functional, the customer has to open Settings, enable each section, pick CLIs, and Save. That's an unnecessary cliff for the common case — most customers want Claude Code and Copilot CLI wired into both MCP servers and the bundled skill pack on day one.

This design changes the default to *everything that doesn't touch user-global state is enabled*, and applies that default to the project tree the first time Concord opens against a project that has no Concord settings file yet. Customers upgrading from 4.0.0 with an existing settings file are not affected — their saved choices remain authoritative.

## Goals

1. Customer installs Concord into a new Mendix project → opens Studio Pro → Concord pane appears already wired: `.mcp.json` written for Claude + Copilot, Concord MCP server running on `:7783` (auto-fallback), skill packs installed under `<project>/.claude/skills/` and `<project>/.github/skills/`. Zero modal clicks required.
2. Existing Concord 4.0.0 users with a saved `terminal-settings.json` are not retroactively re-wired. Their choices stick.
3. Codex remains opt-in. Auto-enabling Codex would write to user-global `~/.codex/config.toml`, which is a side effect outside the project tree we're touching. The customer who wants Codex flips it on in Settings; the customer who doesn't isn't surprised by entries in their home directory.
4. First-run banners surface two pieces of context the customer needs: (a) Studio Pro's own MCP server may not be enabled in Preferences (probe-based detection), in which case the wiring we just wrote is non-functional until they enable it; (b) Maia tools require the Maia panel to stay visible.

## Non-goals

- Probing the customer's `PATH` for installed CLIs and only auto-wiring those that are present. Adds complexity for marginal benefit; both Claude Code and Copilot CLI run inside the Concord terminal which itself sits inside Studio Pro, so "is the CLI installed" isn't really a relevant question — the customer is by definition the kind of person who runs CLI agents.
- Auto-applying defaults on every open. First-run only. Once a settings file exists, Concord respects it.
- Re-applying defaults on Concord upgrade. A customer upgrading 4.0.0 → 4.1.0 keeps their existing settings unchanged; only first-run-on-a-new-project triggers the auto-apply.
- A "preferences wizard" or onboarding modal. The notice banner system already in place is sufficient.
- macOS Maia activation. Maia stays Windows-only; the platform gate keeps `MaiaIntegrationEnabled: true` from doing anything on Mac. The advisory banner only fires on Windows.

## Approach

**Three pieces, all small, all confined to existing surfaces:**

1. Flip `TerminalSettings.Defaults()` so the master toggles return `true` and the per-CLI lists default to Claude + Copilot.
2. Extract the apply-on-save chain (`ApplyMcpConfig` + `ApplyActionsMcpConfig` + `ApplySkillsConfig` + the optional bridge re-create) into a static helper, then call it from a new `TryFirstRunApply()` in `TerminalPaneExtension.Open()` when no settings file exists.
3. Reuse the existing `mcpResult` notice channel for the first-run banners — same transport the modal uses today after Save. Add a Studio-Pro-MCP-disabled probe call and a Maia-pane advisory string-builder.

Considered alternatives:

- **Detect "first run" by probing for any settings file in any version of `terminal-settings.json`.** Same as the proposed approach. Picked because it's the only signal we control.
- **Detect "first run" by version-pinning** — store last-applied-default-version in the settings file, run the apply when current > last. Adds a forward-compat surface for future default flips, but this is the only flip in flight today. YAGNI.
- **Apply defaults silently with no banner.** Rejected — the customer needs to know Studio Pro's MCP must be enabled separately, otherwise the auto-wired `.mcp.json` is a confusing dead URL.

## Architecture

### `TerminalSettings.Defaults()` change ([src/TerminalSettings.cs:21](../../../src/TerminalSettings.cs#L21))

```csharp
public static TerminalSettings Defaults() => new(
    ShellPath: DefaultShellPath(),
    Args: Array.Empty<string>(),
    RingBufferKB: 4096,
    XtermScrollbackLines: 10000,
    Theme: "auto",
    McpEnabled: true,                                        // was false
    McpPort: 8100,
    McpClients: new[] { "claude", "copilot" },               // was Array.Empty<string>()
    McpServerEnabled: true,                                  // was false
    McpServerPort: 7783,
    StudioProActionsEnabled: true,                           // already true
    MaiaIntegrationEnabled: true,                            // already true
    RefreshFromDiskHotkey: "F4",
    RestoreTabsOnReopen: true,
    SkillsEnabled: true,                                     // was false
    SkillClients: new[] { "claude", "copilot" });            // was Array.Empty<string>()
```

The `Defaults()` change *alone* doesn't cause any wiring — it just changes what `Load()` returns when the JSON file is missing. The wiring still requires explicit application against a project directory.

### `SettingsApplyHelper` (new — `src/SettingsApplyHelper.cs`)

The current apply chain lives inline in `TerminalPaneViewModel.HandleSaveSettings`. Extract the prev/next-diff calls to a static helper so the extension can call the same code path without taking a ViewModel dependency:

```csharp
internal static class SettingsApplyHelper
{
    /// <summary>
    /// Applies the diff between two settings snapshots to the project tree:
    /// MCP config files (.mcp.json, ~/.codex/config.toml), Concord MCP server
    /// lifecycle (start/stop), and bundled skill folder install/remove.
    /// Used by both the modal Save flow and the first-run auto-apply path.
    /// </summary>
    /// <returns>Combined "touched" labels for the result banner.</returns>
    public static string[] ApplyAll(
        string projectDir,
        string bundledSkillsRoot,
        TerminalSessionManager manager,
        Logger log,
        TerminalSettings prev,
        TerminalSettings next,
        Func<int> probeStudioProMcpPort,
        StudioProActions actions,
        Maia.MaiaActions? maia);
}
```

The helper encapsulates exactly what `HandleSaveSettings` does today between "compute the new settings" and "save the settings file". `HandleSaveSettings` becomes a thin wrapper that builds `prev`/`next`, calls the helper, then persists the file.

### `TryFirstRunApply` (new method on `TerminalPaneExtension`)

Add a new method on the extension that runs alongside the existing `TryAutoStartActionServer` / `TryRestoreTabsOnFirstOpen`:

```csharp
private void TryFirstRunApply()
{
    var dir = (CurrentApp?.Root as IProject)?.DirectoryPath;
    if (dir is null) return;
    var settingsPath = Path.Combine(dir, "resources", "terminal-settings.json");
    if (File.Exists(settingsPath)) return;  // not first run

    log.Info("[first-run] no settings file — applying defaults to project");
    var defaults = TerminalSettings.Defaults();
    var emptyPrev = TerminalSettings.Defaults() with
    {
        McpEnabled = false,
        McpClients = Array.Empty<string>(),
        McpServerEnabled = false,
        SkillsEnabled = false,
        SkillClients = Array.Empty<string>(),
    };

    // ...build actions/maia/probe like HandleSaveSettings does...
    var touched = SettingsApplyHelper.ApplyAll(dir, bundledSkillsRoot, manager, log,
                                                emptyPrev, defaults,
                                                () => ProbeStudioProMcpPort() ?? defaults.McpPort,
                                                actions, maia);

    defaults.Save(dir);

    // Surface a first-run banner via the active VM's notice channel.
    var vmNotices = BuildFirstRunNotices(defaults, touched);
    foreach (var n in vmNotices) activeViewModel?.PostFirstRunNotice(n);
}
```

`TryFirstRunApply` runs once per project per Concord extension load (the existing pattern of guard-flag fields like `stateRestored`, `subscribed`). Call it from `Open()` immediately after `TryAutoStartActionServer` so the bridge is already up before we wire `.mcp.json` and the skill packs install.

The "empty prev" snapshot is what the apply helper diffs against — the diff sees newly-enabled CLIs and writes wiring; nothing was previously enabled so nothing gets removed.

### `BuildFirstRunNotices` (new helper)

Builds 1–3 strings to show as banners on first run:

```csharp
private string[] BuildFirstRunNotices(TerminalSettings s, string[] touched)
{
    var notices = new List<string>();
    if (touched.Length > 0)
        notices.Add($"Concord wired up for first-time use: {string.Join(", ", touched)}.");

    var spProbe = StudioProThemeProbe.ReadMcpServer(StudioProVersionFromExePath() ?? "");
    if (spProbe.Enabled != true)
        notices.Add("Studio Pro's MCP server appears disabled. Enable it in Edit → Preferences → Maia → MCP Server, then reopen this pane to make the wired CLIs functional.");

    if (OperatingSystem.IsWindows() && s.MaiaIntegrationEnabled)
        notices.Add("Maia tools require the Maia panel to be visible. Keep it open while Claude Code or Copilot CLI drives Maia.");

    return notices.ToArray();
}
```

### `TerminalPaneViewModel.PostFirstRunNotice` (new method)

Thin wrapper that posts an `mcpResult` payload to the JS side with `Ok: true` and the notice text. The modal's existing `bridge.on("mcpResult", ...)` handler renders it as a banner with the existing notice styling. No new transport, no new JS wiring — just a label difference (banners stack visually if multiple notices fire in quick succession; if that's ugly we can serialize them but it's unlikely to matter for first run where the customer sees them in sequence anyway).

### Migration semantics

- **Customer with no `terminal-settings.json` (truly new install or new project)**: `TryFirstRunApply` fires, defaults applied to disk, banners shown, settings file written. Subsequent opens: file exists, no auto-apply.
- **Customer upgrading from Concord 4.0.0 with an existing settings file**: `TryFirstRunApply` early-returns. Their saved choices (mostly all-off because that was 4.0.0's default) remain. They're not auto-upgraded — they have to opt in via the modal as before.
- **Customer upgrading from a 1.x or 2.x file that's missing `skillsEnabled`/`skillClients` keys entirely**: `Load()` migration applies the new defaults via `dto.X ?? def.X`. So an old file missing the keys gets `SkillsEnabled: true` and `SkillClients: ["claude", "copilot"]` in memory. But because the settings file already exists, `TryFirstRunApply` doesn't run — so the in-memory truth is "skills on" but no `.claude/skills/` is actually written until the user opens Settings and saves.
  - This is the one mildly weird edge: the modal shows skills enabled, but until the user saves, the skill folders aren't on disk. Not great UX but not broken either — the next save makes it consistent.
  - Acceptable for v1; if it's a problem we add a one-shot migration that also writes the disk state for these intermediate-version files.
- **Customer upgrading from 1.3.0 with full settings file (all-off)**: Their explicit `mcpEnabled: false` etc. wins over the new defaults. No surprise.

### CHANGELOG positioning

This is v4.1.0 (a feature release on top of 4.0.0). Add the entry above `## 4.0.0 — 2026-05-08`.

## Tests

- `TerminalSettingsTests` — extend with: new `Defaults()` returns the all-on shape; `Load_NoFile_ReturnsAllOnDefaults` verifies the in-memory representation; `Load_LegacyFileWithSkillsOff_StaysOff` verifies a 4.0.0 settings file with `skillsEnabled: false` doesn't get retroactively flipped on.
- `SettingsApplyHelperTests` (new file) — unit tests for the extracted helper, exercising prev/next-diff combinations against a temp project dir. Existing `HandleSaveSettings` integration tests stay valid (the helper is a refactor under the hood).
- `TerminalPaneExtensionTests` — too tightly bound to MEF/Mendix infra to unit-test cleanly; first-run apply gets manual smoke-test coverage via the existing manual-verification checklist (extended below).

Manual verification (extends [README § Development](../../../README.md#development)):

1. Build, deploy to a fresh Mendix project that has no `extensions/Concord/` history. Ensure `<project>/resources/terminal-settings.json` does not exist.
2. Open the project in Studio Pro. Open the Concord pane.
3. Expected within ~1s of pane open:
   - Banner: "Concord wired up for first-time use: …" listing Claude Code, Copilot CLI, both with skills.
   - If Studio Pro's MCP server is disabled in Preferences: a second banner advising to enable it.
   - On Windows with Maia toggled on: a third banner advising to keep the Maia pane open.
4. Verify on disk: `<project>/.mcp.json` contains `mendix-studio-pro` + `concord-mcp` entries; `<project>/.claude/skills/mendix-microflow-update/SKILL.md` exists; `<project>/.github/skills/mendix-microflow-update/SKILL.md` exists; `<project>/resources/terminal-settings.json` exists with all-on values; `<project>/.codex/skills/` does NOT exist; `~/.codex/config.toml` does NOT contain `mendix-studio-pro` or `concord-mcp` entries from this run.
5. Close Studio Pro, reopen the project. Confirm no banners fire (file exists, second-open path).
6. Open Settings → Codex → enable both MCP and Skills, save. Confirm `~/.codex/config.toml` and `<project>/.codex/skills/` are now populated.

## Risks & open questions

- **Customer surprise**: a customer cloning a Mendix project that already has `.mcp.json` from a teammate's Concord run will see the existing entries unchanged (the configurator only upserts our two named entries, leaves others alone). But if they then open Concord and trigger first-run apply, our two entries get refreshed with their local Studio Pro probe port. That's fine — same behavior as a manual save.
- **Probe transient failures**: if `ProbeStudioProMcp` returns null at first-run-apply time (SQLite locked, version detection fails), we fall back to `defaults.McpPort = 8100`. The `.mcp.json` we write may be wrong. Acceptable for v1 — the modal's existing port readout will catch this on the next open and the user can re-save. We log a warning either way.
- **Banner ordering / stacking**: the existing notice channel renders one banner at a time, replacing the previous. Three notices firing in quick succession will visually flash through to the last one. If that's a problem we composite into a single multi-line notice; the existing notice container CSS supports `<br>` content. Bias toward fixing this only if the visual review is unhappy with rapid-fire banners.
- **The `ApplyAll` helper's signature is wide** (`Func<int>`, `StudioProActions`, `Maia.MaiaActions?`). Acceptable because both call sites need exactly these dependencies. If a third call site shows up later we may want a builder.
- **Skill installer races with Concord MCP server start**: both happen during `TryFirstRunApply`. If skill install is slow (lots of disk writes) and the Concord MCP server starts before, that's fine — they're independent. No ordering concern.
