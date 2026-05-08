# Concord Default-On + First-Run Apply Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `TerminalSettings.Defaults()`'s all-off baseline with an all-on baseline (Codex excluded), and add a first-run path in `TerminalPaneExtension.Open()` that applies that baseline to disk so a customer who deploys Concord into a new Mendix project gets MCP wiring, Concord MCP, and skill packs functional with zero clicks.

**Architecture:** Extract the existing apply-on-save chain (`ApplyMcpConfig` + `ApplyActionsMcpConfig` + `ApplySkillsConfig`) from `TerminalPaneViewModel` into a new static `SettingsApplyHelper` so the extension can call the same code path without taking a ViewModel dependency. The bridge (`StudioProActionServer`) lifecycle stays inline in `HandleSaveSettings` and `TryAutoStartActionServer` — first-run apply piggybacks on `TryAutoStartActionServer`'s existing auto-start logic, which fires automatically once the new defaults flip its trigger condition. First-run notices stash in an extension-side queue and flush through the VM's existing `mcpResult` channel when JS sends `"ready"`.

**Tech Stack:** C# 8, .NET 8, xUnit + FluentAssertions for tests, MEF (`[Export]` / `[ImportingConstructor]`) for DI, plain TypeScript for the modal UI (no JS changes in this plan).

**Spec:** [docs/superpowers/specs/2026-05-08-concord-default-on-first-run-apply-design.md](../specs/2026-05-08-concord-default-on-first-run-apply-design.md)

---

## Task 1: Extract apply chain into `SettingsApplyHelper`

**Files:**
- Create: `src/SettingsApplyHelper.cs`
- Modify: `src/TerminalPaneViewModel.cs` (move three Apply methods + `LabelForJson` out, replace with single `SettingsApplyHelper.ApplyAll` call in `HandleSaveSettings`; keep `BuildResultMessage` on the VM since it's display logic)
- Test: `tests/SettingsApplyHelperTests.cs`

**Why:** `TryFirstRunApply` (Task 3) needs to invoke the same prev/next-diff apply logic that `HandleSaveSettings` currently runs inline on the VM. A static helper that takes its dependencies via parameters keeps the extension free of a ViewModel dependency and gives the orchestration layer real test coverage for the first time.

This task is a pure refactor — behavior must be unchanged. Existing tests (`McpJsonConfiguratorTests`, `McpTomlConfiguratorTests`, `SkillInstallerTests`, `TerminalSettingsTests`) must all still pass; the new helper test verifies the extracted orchestration.

- [ ] **Step 1: Write the failing tests for `SettingsApplyHelper`.**

Create `tests/SettingsApplyHelperTests.cs`:

```csharp
using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class SettingsApplyHelperTests : IDisposable
{
    private readonly string tmpRoot;
    private readonly string projectDir;
    private readonly string bundledRoot;
    private readonly Logger log;

    public SettingsApplyHelperTests()
    {
        tmpRoot = Path.Combine(Path.GetTempPath(), "settings-apply-tests-" + Guid.NewGuid().ToString("N"));
        projectDir = Path.Combine(tmpRoot, "project");
        bundledRoot = Path.Combine(tmpRoot, "bundled");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(bundledRoot);
        log = new Logger(projectDir);
        SeedBundledSkill("alpha");
        SeedBundledSkill("beta");
    }

    public void Dispose() => Directory.Delete(tmpRoot, recursive: true);

    private void SeedBundledSkill(string name)
    {
        var d = Path.Combine(bundledRoot, name);
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "SKILL.md"),
            $"---\nname: {name}\ndescription: bundled {name}.\n---\nbody\n");
    }

    private static TerminalSettings AllOff() => TerminalSettings.Defaults() with
    {
        McpEnabled = false,
        McpClients = Array.Empty<string>(),
        McpServerEnabled = false,
        SkillsEnabled = false,
        SkillClients = Array.Empty<string>(),
    };

    private static TerminalSettings ClaudePlusCopilot() => AllOff() with
    {
        McpEnabled = true,
        McpClients = new[] { "claude", "copilot" },
        McpServerEnabled = true,
        SkillsEnabled = true,
        SkillClients = new[] { "claude", "copilot" },
    };

    [Fact]
    public void ApplyAll_PrevAndNextEqual_ReturnsEmptyTouched()
    {
        var s = AllOff();
        var touched = SettingsApplyHelper.ApplyAll(
            projectDir, bundledRoot, s, s, log,
            currentActionServerPort: () => null,
            probeStudioProMcpPort:   () => null);
        touched.Should().BeEmpty();
    }

    [Fact]
    public void ApplyAll_NoneToAll_WritesMcpJsonAndInstallsSkills()
    {
        var prev = AllOff();
        var next = ClaudePlusCopilot();

        var touched = SettingsApplyHelper.ApplyAll(
            projectDir, bundledRoot, prev, next, log,
            currentActionServerPort: () => 7783,
            probeStudioProMcpPort:   () => 8100);

        File.Exists(Path.Combine(projectDir, ".mcp.json")).Should().BeTrue();
        var json = File.ReadAllText(Path.Combine(projectDir, ".mcp.json"));
        json.Should().Contain("mendix-studio-pro");
        json.Should().Contain("concord-mcp");
        json.Should().Contain("8100");
        json.Should().Contain("7783");

        File.Exists(Path.Combine(projectDir, ".claude", "skills", "alpha", "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(projectDir, ".github", "skills", "alpha", "SKILL.md")).Should().BeTrue();

        touched.Should().NotBeEmpty();
    }

    [Fact]
    public void ApplyAll_AllToNone_RemovesMcpJsonAndSkills()
    {
        var prev = ClaudePlusCopilot();
        var next = AllOff();

        // Pre-populate the project so we have something to remove.
        SettingsApplyHelper.ApplyAll(projectDir, bundledRoot, AllOff(), prev, log,
            currentActionServerPort: () => 7783, probeStudioProMcpPort: () => 8100);
        File.Exists(Path.Combine(projectDir, ".mcp.json")).Should().BeTrue();

        var touched = SettingsApplyHelper.ApplyAll(
            projectDir, bundledRoot, prev, next, log,
            currentActionServerPort: () => 7783,
            probeStudioProMcpPort:   () => 8100);

        // .mcp.json should be removed entirely (no other servers in it).
        File.Exists(Path.Combine(projectDir, ".mcp.json")).Should().BeFalse();
        Directory.Exists(Path.Combine(projectDir, ".claude", "skills", "alpha")).Should().BeFalse();
        Directory.Exists(Path.Combine(projectDir, ".github", "skills", "alpha")).Should().BeFalse();
        touched.Should().NotBeEmpty();
    }

    [Fact]
    public void ApplyAll_ProbedPortFallsBackToSavedPort_WhenProbeReturnsNull()
    {
        var prev = AllOff();
        var next = ClaudePlusCopilot() with { McpPort = 9999 };

        SettingsApplyHelper.ApplyAll(
            projectDir, bundledRoot, prev, next, log,
            currentActionServerPort: () => 7783,
            probeStudioProMcpPort:   () => null);  // probe fails

        var json = File.ReadAllText(Path.Combine(projectDir, ".mcp.json"));
        // Falls back to next.McpPort when probe returns null.
        json.Should().Contain("9999");
    }

    [Fact]
    public void ApplyAll_McpEnabledOnly_DoesNotInstallSkills()
    {
        var prev = AllOff();
        var next = AllOff() with
        {
            McpEnabled = true,
            McpClients = new[] { "claude" },
        };

        var touched = SettingsApplyHelper.ApplyAll(
            projectDir, bundledRoot, prev, next, log,
            currentActionServerPort: () => 7783,
            probeStudioProMcpPort:   () => 8100);

        File.Exists(Path.Combine(projectDir, ".mcp.json")).Should().BeTrue();
        Directory.Exists(Path.Combine(projectDir, ".claude", "skills")).Should().BeFalse();
        touched.Should().Contain(t => t.Contains("Claude"));
        touched.Should().NotContain(t => t.Contains("skills"));
    }
}
```

- [ ] **Step 2: Run failing tests.**

```powershell
dotnet test --filter FullyQualifiedName~SettingsApplyHelperTests
```

Expected: build error — `SettingsApplyHelper` does not exist.

- [ ] **Step 3: Create `SettingsApplyHelper` and migrate the three Apply methods.**

Create `src/SettingsApplyHelper.cs`:

```csharp
namespace Terminal;

/// <summary>
/// Encapsulates the apply-on-save chain: writing MCP server entries into
/// <c>.mcp.json</c> / <c>~/.codex/config.toml</c> and installing/uninstalling
/// bundled skill folders into the per-CLI subdirectories. Used by both
/// <see cref="TerminalPaneViewModel"/>'s save handler and
/// <see cref="TerminalPaneExtension"/>'s first-run apply path.
/// </summary>
public static class SettingsApplyHelper
{
    /// <summary>
    /// Apply the diff between <paramref name="prev"/> and <paramref name="next"/>
    /// to the project tree. Returns the list of human-readable "touched"
    /// labels for the result banner.
    /// </summary>
    /// <param name="currentActionServerPort">
    /// Returns the live bound port of the Concord MCP server, or null when
    /// the bridge isn't running. The Concord MCP entry written into
    /// <c>.mcp.json</c> uses this live port (with fallback to
    /// <c>next.McpServerPort</c>).
    /// </param>
    /// <param name="probeStudioProMcpPort">
    /// Returns Studio Pro's actual MCP-server port, probed live from
    /// <c>Settings.sqlite</c>, or null when the probe fails. The
    /// <c>mendix-studio-pro</c> entry written into <c>.mcp.json</c> uses
    /// this port (with fallback to <c>next.McpPort</c>).
    /// </param>
    public static string[] ApplyAll(
        string projectDir,
        string bundledSkillsRoot,
        TerminalSettings prev,
        TerminalSettings next,
        Logger log,
        Func<int?> currentActionServerPort,
        Func<int?> probeStudioProMcpPort)
    {
        var touched = new List<string>();
        touched.AddRange(ApplyMcpConfig(projectDir, prev, next, log, probeStudioProMcpPort));
        touched.AddRange(ApplyActionsMcpConfig(projectDir, prev, next, log, currentActionServerPort));
        touched.AddRange(ApplySkillsConfig(projectDir, bundledSkillsRoot, prev, next, log));
        return touched.ToArray();
    }

    /// <summary>
    /// Diff between previous and new MCP settings, written to <c>.mcp.json</c>
    /// (Claude Code + Copilot CLI) and <c>~/.codex/config.toml</c> (Codex).
    /// Mirrors the behavior previously inline on TerminalPaneViewModel.
    /// </summary>
    private static string[] ApplyMcpConfig(
        string projectDir,
        TerminalSettings prev,
        TerminalSettings next,
        Logger log,
        Func<int?> probeStudioProMcpPort)
    {
        var prevClients = new HashSet<string>(prev.McpClients, StringComparer.OrdinalIgnoreCase);
        var nextClients = next.McpEnabled
            ? new HashSet<string>(next.McpClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var jsonNeeded = nextClients.Contains("claude") || nextClients.Contains("copilot");
        var jsonHadIt  = prev.McpEnabled && (prevClients.Contains("claude") || prevClients.Contains("copilot"));
        var tomlNeeded = nextClients.Contains("codex");
        var tomlHadIt  = prev.McpEnabled && prevClients.Contains("codex");

        var probedPort = probeStudioProMcpPort() ?? next.McpPort;
        var url = $"http://localhost:{probedPort}/mcp";
        var json = new McpJsonConfigurator(projectDir);
        var toml = new McpTomlConfigurator();
        var touched = new List<string>();

        log.Info($"[mcp-config] primary diff jsonNeeded={jsonNeeded} jsonHadIt={jsonHadIt} tomlNeeded={tomlNeeded} tomlHadIt={tomlHadIt} url={url}");

        try
        {
            if (jsonNeeded) { json.Upsert(url); log.Info($"[mcp-config-json] upserted {McpJsonConfigurator.ServerName} -> {url}"); touched.Add(LabelForJson(nextClients)); }
            else if (jsonHadIt) { json.Remove(); log.Info($"[mcp-config-json] removed {McpJsonConfigurator.ServerName}"); touched.Add(LabelForJson(prevClients) + " (removed)"); }
        }
        catch (Exception ex) { log.Error("[mcp-config-json] primary write failed", ex); }

        try
        {
            if (tomlNeeded) { toml.Upsert(url); log.Info($"[mcp-config-toml] upserted {McpTomlConfigurator.ServerName} -> {url} at {toml.FilePath}"); touched.Add("Codex"); }
            else if (tomlHadIt) { toml.Remove(); log.Info($"[mcp-config-toml] removed {McpTomlConfigurator.ServerName}"); touched.Add("Codex (removed)"); }
        }
        catch (Exception ex) { log.Error("[mcp-config-toml] primary write failed", ex); }

        return touched.ToArray();
    }

    /// <summary>
    /// Diff for the Concord MCP entry (the in-process action server).
    /// Mirrors the behavior previously inline on TerminalPaneViewModel.
    /// </summary>
    private static string[] ApplyActionsMcpConfig(
        string projectDir,
        TerminalSettings prev,
        TerminalSettings next,
        Logger log,
        Func<int?> currentActionServerPort)
    {
        var prevClients = new HashSet<string>(prev.McpClients, StringComparer.OrdinalIgnoreCase);
        var nextClients = next.McpServerEnabled
            ? new HashSet<string>(next.McpClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var jsonNeeded = nextClients.Contains("claude") || nextClients.Contains("copilot");
        var jsonHadIt  = prev.McpServerEnabled && (prevClients.Contains("claude") || prevClients.Contains("copilot"));
        var tomlNeeded = nextClients.Contains("codex");
        var tomlHadIt  = prev.McpServerEnabled && prevClients.Contains("codex");

        var port = currentActionServerPort() ?? next.McpServerPort;
        var url = $"http://localhost:{port}/mcp";
        var json = new McpJsonConfigurator(projectDir);
        var toml = new McpTomlConfigurator();
        var touched = new List<string>();

        log.Info($"[mcp-config] actions diff jsonNeeded={jsonNeeded} jsonHadIt={jsonHadIt} tomlNeeded={tomlNeeded} tomlHadIt={tomlHadIt} url={url} live-port={currentActionServerPort()?.ToString() ?? "null"}");

        try
        {
            if (jsonNeeded) { json.UpsertActions(url); log.Info($"[mcp-config-json] upserted {McpJsonConfigurator.ActionsServerName} -> {url}"); touched.Add(LabelForJson(nextClients) + " actions"); }
            else if (jsonHadIt) { json.RemoveActions(); log.Info($"[mcp-config-json] removed {McpJsonConfigurator.ActionsServerName}"); touched.Add(LabelForJson(prevClients) + " actions (removed)"); }
        }
        catch (Exception ex) { log.Error("[mcp-config-json] actions write failed", ex); }

        try
        {
            if (tomlNeeded) { toml.UpsertActions(url); log.Info($"[mcp-config-toml] upserted {McpTomlConfigurator.ActionsServerName} -> {url} at {toml.FilePath}"); touched.Add("Codex actions"); }
            else if (tomlHadIt) { toml.RemoveActions(); log.Info($"[mcp-config-toml] removed {McpTomlConfigurator.ActionsServerName}"); touched.Add("Codex actions (removed)"); }
        }
        catch (Exception ex) { log.Error("[mcp-config-toml] actions write failed", ex); }

        return touched.ToArray();
    }

    /// <summary>
    /// Diff for skill packs: install bundled folders for newly-selected
    /// CLIs, remove for newly-deselected CLIs. Mirrors the behavior
    /// previously inline on TerminalPaneViewModel.
    /// </summary>
    private static string[] ApplySkillsConfig(
        string projectDir,
        string bundledSkillsRoot,
        TerminalSettings prev,
        TerminalSettings next,
        Logger log)
    {
        var prevClients = prev.SkillsEnabled
            ? new HashSet<string>(prev.SkillClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextClients = next.SkillsEnabled
            ? new HashSet<string>(next.SkillClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var installer = new SkillInstaller(projectDir, bundledSkillsRoot, log);
        var touched = new List<string>();

        var perCli = new (string Key, string Label, string Subdir)[]
        {
            ("claude",  "Claude Code skills",       Path.Combine(".claude", "skills")),
            ("copilot", "Copilot CLI skills",       Path.Combine(".github", "skills")),
            ("codex",   "Codex skills",             Path.Combine(".codex",  "skills")),
        };

        log.Info($"[skills] diff prev={{{string.Join(",", prevClients)}}} next={{{string.Join(",", nextClients)}}} bundled-root={bundledSkillsRoot}");

        foreach (var (key, label, subdir) in perCli)
        {
            var was = prevClients.Contains(key);
            var now = nextClients.Contains(key);
            try
            {
                if (now && !was)       { installer.InstallAll(subdir); touched.Add(label); }
                else if (now && was)   { installer.InstallAll(subdir); /* refresh on every save */ }
                else if (!now && was)  { installer.RemoveAll(subdir);  touched.Add(label + " (removed)"); }
            }
            catch (Exception ex)
            {
                log.Error($"[skills] {label} apply failed", ex);
            }
        }

        return touched.ToArray();
    }

    private static string LabelForJson(HashSet<string> clients)
    {
        var parts = new List<string>();
        if (clients.Contains("claude"))  parts.Add("Claude Code");
        if (clients.Contains("copilot")) parts.Add("Copilot CLI");
        return string.Join(" + ", parts);
    }
}
```

- [ ] **Step 4: Update `TerminalPaneViewModel` to call the helper.**

Edit `src/TerminalPaneViewModel.cs`. Find the section in `HandleSaveSettings` that currently calls the three Apply methods (around line 312):

Old:

```csharp
            // 3. Apply file changes BEFORE saving settings.
            var touchedPrimary = ApplyMcpConfig(dir, current, updated);
            var touchedActions = ApplyActionsMcpConfig(dir, current, updated);
            var touchedSkills  = ApplySkillsConfig(dir, current, updated);

            updated.Save(dir);
            Post("settings", BuildSettingsPayload(updated));

            var allTouched = touchedPrimary.Concat(touchedActions).Concat(touchedSkills).ToArray();
```

Replace with:

```csharp
            // 3. Apply file changes BEFORE saving settings.
            var allTouched = SettingsApplyHelper.ApplyAll(
                dir,
                bundledSkillsRoot,
                current,
                updated,
                log,
                currentActionServerPort: () => manager.CurrentActionServerPort,
                probeStudioProMcpPort:   () => ProbeStudioProMcp()?.Port);

            updated.Save(dir);
            Post("settings", BuildSettingsPayload(updated));
```

In the same file, delete the three private methods that are now obsolete: `ApplyMcpConfig`, `ApplyActionsMcpConfig`, `ApplySkillsConfig`, and the private `LabelForJson` helper. They live in `SettingsApplyHelper` now.

Keep `BuildResultMessage` on the VM — it's display logic, not core wiring.

- [ ] **Step 5: Run all tests.**

```powershell
dotnet test
```

Expected: all existing tests pass plus the 5 new `SettingsApplyHelperTests` (147 total). No regressions.

- [ ] **Step 6: Commit.**

```powershell
git add src/SettingsApplyHelper.cs src/TerminalPaneViewModel.cs tests/SettingsApplyHelperTests.cs
git commit -m "refactor(settings): extract apply chain into SettingsApplyHelper"
```

---

## Task 2: Flip `TerminalSettings.Defaults()` to all-on (Codex excluded)

**Files:**
- Modify: `src/TerminalSettings.cs`
- Modify: `tests/TerminalSettingsTests.cs`

- [ ] **Step 1: Write failing tests for the new defaults.**

Edit `tests/TerminalSettingsTests.cs`. Find the existing `Load_NoFile_ReturnsDefaults` test (around line 19) and update its assertions. Also add three new tests. Replace the existing `Load_NoFile_ReturnsDefaults` body with:

```csharp
[Fact]
public void Load_NoFile_ReturnsDefaults()
{
    var settings = TerminalSettings.Load(tmpDir);
    if (OperatingSystem.IsWindows())
        settings.ShellPath.Should().Be("powershell.exe");
    else
        settings.ShellPath.Should().NotBeNullOrEmpty();
    settings.Args.Should().BeEmpty();
    settings.RingBufferKB.Should().Be(4096);
    settings.XtermScrollbackLines.Should().Be(10000);
    settings.Theme.Should().Be("auto");
    // Defaults flipped in v4.1.0: all on except Codex (which writes to
    // user-global ~/.codex/config.toml — opt-in only).
    settings.McpEnabled.Should().BeTrue();
    settings.McpPort.Should().Be(8100);
    settings.McpClients.Should().BeEquivalentTo(new[] { "claude", "copilot" });
    settings.McpServerEnabled.Should().BeTrue();
    settings.StudioProActionsEnabled.Should().BeTrue();
    settings.MaiaIntegrationEnabled.Should().BeTrue();
    settings.SkillsEnabled.Should().BeTrue();
    settings.SkillClients.Should().BeEquivalentTo(new[] { "claude", "copilot" });
}
```

Find the existing `Load_NoFile_HasSkillsDisabledAndNoClients` test (added in the v4.0.0 work) and replace with:

```csharp
[Fact]
public void Load_NoFile_AllOnExceptCodex()
{
    var settings = TerminalSettings.Load(tmpDir);
    settings.SkillsEnabled.Should().BeTrue();
    settings.SkillClients.Should().BeEquivalentTo(new[] { "claude", "copilot" });
    settings.SkillClients.Should().NotContain("codex");
}
```

Add these three new tests to the same class:

```csharp
[Fact]
public void Load_LegacyFileWithExplicitFalse_StaysOff()
{
    // A 4.0.0 settings file where the user explicitly chose to disable
    // skills. The new v4.1.0 defaults must NOT retroactively flip them on.
    var resourcesDir = Path.Combine(tmpDir, "resources");
    Directory.CreateDirectory(resourcesDir);
    File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"), """
        {
          "shellPath": "powershell.exe",
          "args": [],
          "ringBufferKB": 4096,
          "xtermScrollbackLines": 10000,
          "theme": "auto",
          "mcpEnabled": false,
          "mcpPort": 8100,
          "mcpClients": [],
          "mcpServerEnabled": false,
          "mcpServerPort": 7783,
          "studioProActionsEnabled": true,
          "maiaIntegrationEnabled": true,
          "refreshFromDiskHotkey": "F4",
          "restoreTabsOnReopen": true,
          "skillsEnabled": false,
          "skillClients": []
        }
        """);
    var settings = TerminalSettings.Load(tmpDir);
    settings.McpEnabled.Should().BeFalse();
    settings.McpServerEnabled.Should().BeFalse();
    settings.SkillsEnabled.Should().BeFalse();
    settings.SkillClients.Should().BeEmpty();
}

[Fact]
public void Load_VeryOldFileMissingSkillKeys_DefaultsToOnViaMigration()
{
    // A 1.3.x settings file without skillsEnabled/skillClients keys.
    // Null-coalescing in Load() picks up the new v4.1.0 defaults for those
    // keys (this is acceptable per the spec — the in-memory representation
    // says "skills on" but disk is unchanged until next Save).
    var resourcesDir = Path.Combine(tmpDir, "resources");
    Directory.CreateDirectory(resourcesDir);
    File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"), """
        {
          "shellPath": "powershell.exe",
          "args": [],
          "ringBufferKB": 4096,
          "xtermScrollbackLines": 10000,
          "theme": "auto",
          "mcpEnabled": true,
          "mcpPort": 8100,
          "mcpClients": ["claude"],
          "mcpServerEnabled": true,
          "mcpServerPort": 7783,
          "studioProActionsEnabled": true,
          "maiaIntegrationEnabled": true,
          "refreshFromDiskHotkey": "F4",
          "restoreTabsOnReopen": true
        }
        """);
    var settings = TerminalSettings.Load(tmpDir);
    settings.McpEnabled.Should().BeTrue();
    settings.SkillsEnabled.Should().BeTrue();
    settings.SkillClients.Should().BeEquivalentTo(new[] { "claude", "copilot" });
}

[Fact]
public void Defaults_DoesNotIncludeCodex()
{
    var d = TerminalSettings.Defaults();
    d.McpClients.Should().NotContain("codex");
    d.SkillClients.Should().NotContain("codex");
}
```

- [ ] **Step 2: Run failing tests.**

```powershell
dotnet test --filter FullyQualifiedName~TerminalSettingsTests
```

Expected: 3 new tests fail; the existing `Load_NoFile_ReturnsDefaults` and `Load_NoFile_AllOnExceptCodex` tests fail (they assert the new defaults that don't exist yet).

- [ ] **Step 3: Flip the defaults.**

Edit `src/TerminalSettings.cs`. Find `Defaults()` (around line 21) and replace:

Old:

```csharp
    public static TerminalSettings Defaults() => new(
        ShellPath: DefaultShellPath(),
        Args: Array.Empty<string>(),
        RingBufferKB: 4096,
        XtermScrollbackLines: 10000,
        Theme: "auto",
        McpEnabled: false,
        McpPort: 8100,
        McpClients: Array.Empty<string>(),
        McpServerEnabled: false,
        McpServerPort: 7783,
        StudioProActionsEnabled: true,
        MaiaIntegrationEnabled: true,
        RefreshFromDiskHotkey: "F4",
        RestoreTabsOnReopen: true,
        SkillsEnabled: false,
        SkillClients: Array.Empty<string>());
```

New:

```csharp
    public static TerminalSettings Defaults() => new(
        ShellPath: DefaultShellPath(),
        Args: Array.Empty<string>(),
        RingBufferKB: 4096,
        XtermScrollbackLines: 10000,
        Theme: "auto",
        // v4.1.0: all-on except Codex. Codex writes to user-global
        // ~/.codex/config.toml — keeping it opt-in avoids touching state
        // outside the project tree without explicit consent.
        McpEnabled: true,
        McpPort: 8100,
        McpClients: new[] { "claude", "copilot" },
        McpServerEnabled: true,
        McpServerPort: 7783,
        StudioProActionsEnabled: true,
        MaiaIntegrationEnabled: true,
        RefreshFromDiskHotkey: "F4",
        RestoreTabsOnReopen: true,
        SkillsEnabled: true,
        SkillClients: new[] { "claude", "copilot" });
```

- [ ] **Step 4: Run tests to verify they pass.**

```powershell
dotnet test --filter FullyQualifiedName~TerminalSettingsTests
```

Expected: all `TerminalSettingsTests` pass. If any other test class regressed (e.g. a test elsewhere asserts `McpEnabled.Should().BeFalse()` on a freshly-defaulted settings), update those assertions to reflect the new defaults — they were written against the old behavior.

- [ ] **Step 5: Run the full suite.**

```powershell
dotnet test
```

Expected: 147 + 3 = 150 passing, no regressions.

- [ ] **Step 6: Commit.**

```powershell
git add src/TerminalSettings.cs tests/TerminalSettingsTests.cs
git commit -m "feat(settings): flip Defaults() to all-on (Codex excluded)"
```

---

## Task 3: Add `TryFirstRunApply` to `TerminalPaneExtension`

**Files:**
- Modify: `src/TerminalPaneExtension.cs`

**Why:** First-run detection lives at extension level — it's the layer that knows about the project directory at `Open()` time. The extension calls `SettingsApplyHelper.ApplyAll` (built in Task 1) with an "empty prev" snapshot vs the current `Defaults()` (now all-on after Task 2), so the first save writes `.mcp.json`, installs skill packs, and persists the settings file. The Concord MCP bridge starts via the existing `TryAutoStartActionServer` path, which sees the new defaults and fires automatically — no bridge work needed in `TryFirstRunApply` itself.

The notice queue is a simple `List<string>` field on the extension. Task 4 wires it through to the VM. This task only populates it.

- [ ] **Step 1: Add `TryFirstRunApply` and supporting methods.**

Edit `src/TerminalPaneExtension.cs`. Add a new field near the other private fields (around line 22-34):

```csharp
    /// <summary>
    /// First-run notices queued by <see cref="TryFirstRunApply"/>, drained by
    /// the VM's "ready" handler via the consume-Func passed into its
    /// constructor. List rather than array because the VM consumes once and
    /// clears.
    /// </summary>
    private readonly List<string> pendingFirstRunNotices = new();
    private bool firstRunChecked;
```

Add a new private method near the other `Try*` methods (e.g. after `TryAutoStartActionServer`):

```csharp
    /// <summary>
    /// On a project that has never had Concord settings persisted, write the
    /// new (v4.1.0) defaults to disk: <c>.mcp.json</c> for Claude + Copilot,
    /// bundled skill folders into <c>.claude/skills</c> and
    /// <c>.github/skills</c>, plus the settings file itself. The Concord MCP
    /// bridge is started separately by <see cref="TryAutoStartActionServer"/>
    /// (which runs first, sees the new defaults, and fires automatically).
    /// Queues advisory notices for the VM to flush once the JS side is ready.
    /// </summary>
    private void TryFirstRunApply()
    {
        if (firstRunChecked) return;
        firstRunChecked = true;

        try
        {
            var dir = (CurrentApp?.Root as IProject)?.DirectoryPath;
            if (dir is null) return;

            var settingsPath = Path.Combine(dir, "resources", "terminal-settings.json");
            if (File.Exists(settingsPath))
            {
                log.Info("[first-run] settings file exists — skipping auto-apply");
                return;
            }

            log.Info("[first-run] no settings file — applying defaults to project");
            var defaults = TerminalSettings.Defaults();

            // "Empty prev" so the apply chain treats every CLI as newly-added
            // and writes/installs accordingly.
            var emptyPrev = defaults with
            {
                McpEnabled = false,
                McpClients = Array.Empty<string>(),
                McpServerEnabled = false,
                SkillsEnabled = false,
                SkillClients = Array.Empty<string>(),
            };

            var bundledSkillsRoot = extensionFileService.ResolvePath("skills");

            var touched = SettingsApplyHelper.ApplyAll(
                dir,
                bundledSkillsRoot,
                emptyPrev,
                defaults,
                log,
                currentActionServerPort: () => manager.CurrentActionServerPort,
                probeStudioProMcpPort:   () => ProbeStudioProMcpPort());

            defaults.Save(dir);

            pendingFirstRunNotices.AddRange(BuildFirstRunNotices(defaults, touched));
            log.Info($"[first-run] applied {touched.Length} target(s); queued {pendingFirstRunNotices.Count} notice(s)");
        }
        catch (Exception ex)
        {
            log.Error("[first-run] apply failed", ex);
        }
    }

    /// <summary>
    /// Build the 1–3 advisory strings shown to the user on first run. The
    /// "Concord wired up" line summarizes what was applied; the SP-MCP and
    /// Maia advisories surface configuration the customer needs to handle
    /// outside Concord.
    /// </summary>
    private string[] BuildFirstRunNotices(TerminalSettings s, string[] touched)
    {
        var notices = new List<string>();
        if (touched.Length > 0)
            notices.Add($"Concord wired up for first-time use: {string.Join(", ", touched)}.");

        try
        {
            var version = StudioProVersionFromExePath();
            if (!string.IsNullOrEmpty(version))
            {
                var info = StudioProThemeProbe.ReadMcpServer(version);
                if (info.Enabled != true)
                    notices.Add("Studio Pro's MCP server appears disabled. Enable it in Edit → Preferences → Maia → MCP Server, then reopen this pane to make the wired CLI configs functional.");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"[first-run] SP-MCP probe failed: {ex.Message}");
        }

        if (OperatingSystem.IsWindows() && s.MaiaIntegrationEnabled)
            notices.Add("Maia tools require the Maia panel to be visible. Keep it open while Claude Code or Copilot CLI drives Maia.");

        return notices.ToArray();
    }

    /// <summary>
    /// Probe Studio Pro's MCP server port from <c>Settings.sqlite</c>.
    /// Returns null on probe failure (caller falls back to the saved port).
    /// Lives at extension level so both <see cref="TryFirstRunApply"/> and
    /// the VM's save flow can use it.
    /// </summary>
    private int? ProbeStudioProMcpPort()
    {
        try
        {
            var version = StudioProVersionFromExePath();
            if (string.IsNullOrEmpty(version)) return null;
            var info = StudioProThemeProbe.ReadMcpServer(version);
            return info.Port;
        }
        catch
        {
            return null;
        }
    }
```

Find `Open()` (around line 43) and add the `TryFirstRunApply()` call. The existing flow is:

```csharp
        EnsureLogger();
        cachedProjectDir = ...;
        EnsureLifecycleSubscribed();
        EnsureStatePersistenceHooked();
        EnsureManagerForwardingHooked();
        TryAutoStartActionServer();
        TryRestoreTabsOnFirstOpen();
```

Add `TryFirstRunApply()` AFTER `TryAutoStartActionServer()` so that:
- TryAutoStartActionServer reads the file (or Defaults() when file is missing), sees `McpServerEnabled = true` from the new defaults, and starts the bridge — making `manager.CurrentActionServerPort` non-null before TryFirstRunApply runs.
- TryFirstRunApply then writes `.mcp.json` with the live port.

New flow:

```csharp
        EnsureLogger();
        cachedProjectDir = ...;
        EnsureLifecycleSubscribed();
        EnsureStatePersistenceHooked();
        EnsureManagerForwardingHooked();
        TryAutoStartActionServer();
        TryFirstRunApply();
        TryRestoreTabsOnFirstOpen();
```

- [ ] **Step 2: Build to verify the new code compiles.**

```powershell
dotnet build
```

Expected: build succeeds. If `extensionFileService` isn't yet a field on `TerminalPaneExtension`, verify that the v4.0.0 work to inject it via `[ImportingConstructor]` is in place (it should be — landed in commit `3feb2e1`).

- [ ] **Step 3: Run all tests.**

```powershell
dotnet test
```

Expected: all 150 tests pass. `TerminalPaneExtension` is intentionally not unit-tested (MEF/Mendix infra coupling); the manual smoke test in Task 5 verifies behavior.

- [ ] **Step 4: Commit.**

```powershell
git add src/TerminalPaneExtension.cs
git commit -m "feat(extension): TryFirstRunApply writes defaults on a fresh project"
```

---

## Task 4: Wire first-run notices through the VM's `ready` handler

**Files:**
- Modify: `src/TerminalPaneViewModel.cs`
- Modify: `src/TerminalPaneExtension.cs`

**Why:** `TryFirstRunApply` (Task 3) populates `pendingFirstRunNotices` on the extension. The VM needs to drain that list and post each notice via the existing `mcpResult` channel — but only after the JS side has loaded and is ready to render banners. The "ready" message that arrives in the VM's `OnWebViewMessage` handler is the canonical signal.

Decoupling is via a `Func<string[]>` passed into the VM constructor: the VM calls it once, gets the queued notices, posts them. The Func captures a reference to the extension's list and clears it when called (consume-once semantics).

- [ ] **Step 1: Add the consume-Func to `TerminalPaneViewModel`.**

Edit `src/TerminalPaneViewModel.cs`. Find the field declarations and constructor (around lines 16-50). Add a new readonly field after `bundledSkillsRoot`:

```csharp
    private readonly string bundledSkillsRoot;
    private readonly Func<string[]> consumePendingFirstRunNotices;
```

Update the constructor signature. Old:

```csharp
    public TerminalPaneViewModel(
        string title,
        TerminalSessionManager manager,
        Func<IModel?> getCurrentApp,
        Uri webIndexUri,
        Logger log,
        Func<string?> getApplicationRootUrl,
        string bundledSkillsRoot)
    {
        Title = title;
        this.manager = manager;
        this.getCurrentApp = getCurrentApp;
        this.webIndexUri = webIndexUri;
        this.log = log;
        this.getApplicationRootUrl = getApplicationRootUrl;
        this.bundledSkillsRoot = bundledSkillsRoot;
    }
```

New:

```csharp
    public TerminalPaneViewModel(
        string title,
        TerminalSessionManager manager,
        Func<IModel?> getCurrentApp,
        Uri webIndexUri,
        Logger log,
        Func<string?> getApplicationRootUrl,
        string bundledSkillsRoot,
        Func<string[]> consumePendingFirstRunNotices)
    {
        Title = title;
        this.manager = manager;
        this.getCurrentApp = getCurrentApp;
        this.webIndexUri = webIndexUri;
        this.log = log;
        this.getApplicationRootUrl = getApplicationRootUrl;
        this.bundledSkillsRoot = bundledSkillsRoot;
        this.consumePendingFirstRunNotices = consumePendingFirstRunNotices;
    }
```

- [ ] **Step 2: Flush notices on the JS-side `ready` event.**

Find `OnWebViewMessage` (around line 97). The "ready" case currently looks like:

```csharp
                case "ready":
                case "listTabs":
                    Post("tabsList", new TabsListPayload(
                        manager.ListSessions().Select(s => new SessionInfoPayload(s.TabId, s.Title, s.ShellPath, s.Cwd, s.Alive)).ToList()
                    ));
                    break;
```

Update to flush queued notices on "ready" (but not on "listTabs" — only the initial ready event). Replace with:

```csharp
                case "ready":
                    Post("tabsList", new TabsListPayload(
                        manager.ListSessions().Select(s => new SessionInfoPayload(s.TabId, s.Title, s.ShellPath, s.Cwd, s.Alive)).ToList()
                    ));
                    FlushPendingFirstRunNotices();
                    break;

                case "listTabs":
                    Post("tabsList", new TabsListPayload(
                        manager.ListSessions().Select(s => new SessionInfoPayload(s.TabId, s.Title, s.ShellPath, s.Cwd, s.Alive)).ToList()
                    ));
                    break;
```

Add the helper method near the bottom of the class (e.g. above `BuildSettingsPayload`):

```csharp
    /// <summary>
    /// Pull queued first-run notices from the extension and surface each one
    /// as an <c>mcpResult</c> banner. Idempotent — the consume-Func clears
    /// the queue on first call, so subsequent invocations no-op.
    /// </summary>
    private void FlushPendingFirstRunNotices()
    {
        try
        {
            var notices = consumePendingFirstRunNotices();
            foreach (var notice in notices)
            {
                Post("mcpResult", new Messages.McpResultPayload(true, notice, Array.Empty<string>()));
                log.Info($"[first-run] flushed notice: {notice}");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"[first-run] flush failed: {ex.Message}");
        }
    }
```

- [ ] **Step 3: Wire the consume-Func from the extension.**

Edit `src/TerminalPaneExtension.cs`. Find the `TerminalPaneViewModel` constructor call in `Open()` (added during the v4.0.0 Skills work). Old:

```csharp
        var vm = new TerminalPaneViewModel(
            title: "Concord",
            manager: manager,
            getCurrentApp: () => CurrentApp,
            webIndexUri: indexUri,
            log: log,
            getApplicationRootUrl: () =>
            {
                var model = CurrentApp;
                if (model is null) return null;
                try { return localRunConfigs.GetActiveConfiguration(model)?.ApplicationRootUrl; }
                catch (Exception ex) { log?.Warn($"GetActiveConfiguration threw: {ex.Message}"); return null; }
            },
            bundledSkillsRoot: bundledSkillsRoot);
```

New:

```csharp
        var vm = new TerminalPaneViewModel(
            title: "Concord",
            manager: manager,
            getCurrentApp: () => CurrentApp,
            webIndexUri: indexUri,
            log: log,
            getApplicationRootUrl: () =>
            {
                var model = CurrentApp;
                if (model is null) return null;
                try { return localRunConfigs.GetActiveConfiguration(model)?.ApplicationRootUrl; }
                catch (Exception ex) { log?.Warn($"GetActiveConfiguration threw: {ex.Message}"); return null; }
            },
            bundledSkillsRoot: bundledSkillsRoot,
            consumePendingFirstRunNotices: () =>
            {
                var notices = pendingFirstRunNotices.ToArray();
                pendingFirstRunNotices.Clear();
                return notices;
            });
```

- [ ] **Step 4: Build to verify.**

```powershell
dotnet build
```

Expected: build succeeds (constructor signature change matched at the single call site).

- [ ] **Step 5: Run all tests.**

```powershell
dotnet test
```

Expected: 150 passing, no regressions. (No new tests for this task — the wiring is integration-scoped and exercised by the manual smoke test in Task 5.)

- [ ] **Step 6: Commit.**

```powershell
git add src/TerminalPaneViewModel.cs src/TerminalPaneExtension.cs
git commit -m "feat(extension): flush first-run notices when JS side becomes ready"
```

---

## Task 5: Manual end-to-end smoke test

**Files:** None — documentation step. Capture results in your local notes; do not commit.

- [ ] **Step 1: Deploy Concord to a fresh test project.**

In `Directory.Build.props`, set `MendixDeployTarget` to a Mendix 11.10+ project that has never had Concord installed (or delete `<project>/extensions/Concord/`, `<project>/.mcp.json`, `<project>/.claude/`, `<project>/.github/`, `<project>/resources/terminal-settings.json` from a previous test). Then:

```powershell
dotnet build
```

- [ ] **Step 2: Open the project in Studio Pro and the Concord pane.**

Expected within ~1s of pane open:

- The Concord MCP bridge is running (check `localhost:7783` via curl or the diagnostic in DEPLOYING.md § "Concord MCP tools time out").
- A first-run banner appears: `"Concord wired up for first-time use: Claude Code + Copilot CLI, Claude Code + Copilot CLI actions, Claude Code skills, Copilot CLI skills."` (or whichever subset got applied).
- If Studio Pro's MCP server is disabled in Edit → Preferences → Maia → MCP Server, a second banner advises to enable it.
- On Windows: a third banner advises to keep the Maia pane open.

- [ ] **Step 3: Verify on disk.**

```powershell
Test-Path "<project>\.mcp.json"                                                # True
(Get-Content "<project>\.mcp.json") -match "mendix-studio-pro"                 # True
(Get-Content "<project>\.mcp.json") -match "concord-mcp"                       # True
Test-Path "<project>\.claude\skills\mendix-microflow-update\SKILL.md"          # True
Test-Path "<project>\.github\skills\mendix-microflow-update\SKILL.md"          # True
Test-Path "<project>\.codex\skills"                                            # False  (Codex is opt-in)
Test-Path "<project>\resources\terminal-settings.json"                         # True
```

Open `<project>\resources\terminal-settings.json` and confirm `mcpEnabled: true`, `mcpClients: ["claude", "copilot"]`, `skillsEnabled: true`, `skillClients: ["claude", "copilot"]`.

Verify `~/.codex/config.toml` does NOT contain a `mendix-studio-pro` or `concord-mcp` entry from this run (Codex is opt-in).

- [ ] **Step 4: Reopen the project — confirm no banner spam.**

Close Studio Pro. Reopen the same project. Open the Concord pane.

Expected: NO first-run banners (settings file exists, second-open path). The bridge still auto-starts because `TryAutoStartActionServer` reads the saved settings.

- [ ] **Step 5: Verify Codex is still opt-in.**

In Concord settings → Studio Pro MCP → tick Codex → Save. Confirm `~/.codex/config.toml` now contains `[mcp_servers.mendix-studio-pro]`. In settings → Skills → tick Codex → Save. Confirm `<project>/.codex/skills/mendix-microflow-update/SKILL.md` now exists.

- [ ] **Step 6: Verify legacy-settings non-regression.**

On a different project that has a 4.0.0-era `terminal-settings.json` saved with `mcpEnabled: false`, open the Concord pane. The settings stay disabled (no auto-flip-on). `TryFirstRunApply` is a no-op because the file already exists.

---

## Task 6: Bump version to 4.1.0 and add CHANGELOG entry

**Files:**
- Modify: `Terminal.csproj`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Bump csproj version.**

Edit `Terminal.csproj`. Find the `<Version>` and `<InformationalVersion>` lines (around 15-16):

Old:

```xml
    <Version>4.0.0</Version>
    <InformationalVersion>4.0.0+bundled-skills</InformationalVersion>
```

New:

```xml
    <Version>4.1.0</Version>
    <InformationalVersion>4.1.0+default-on-first-run</InformationalVersion>
```

- [ ] **Step 2: Add CHANGELOG entry.**

Edit `CHANGELOG.md`. Add a new section above the existing `## 4.0.0 — 2026-05-08`:

```markdown
## 4.1.0 — 2026-05-08

### Added

- **Default-on settings + first-run auto-apply.** `TerminalSettings.Defaults()` now returns all toggles on (Claude Code + Copilot CLI for both MCP families and skills). When Concord opens against a project that has no `resources/terminal-settings.json` yet, it writes `.mcp.json`, installs bundled skill packs into `.claude/skills/` and `.github/skills/`, and persists the settings file in one go — no modal clicks required. The Concord MCP bridge starts automatically through the existing `TryAutoStartActionServer` path now that the trigger condition is on by default.
- **First-run advisory banners.** On a fresh install, three banners appear via the existing `mcpResult` channel: a "Concord wired up" summary, a Studio Pro MCP-disabled advisory (if the SQLite probe shows it disabled in Preferences), and a Maia-pane-open advisory on Windows.

### Notes

- **Codex stays opt-in.** Auto-enabling Codex would write to user-global `~/.codex/config.toml` and `~/.codex/skills/` (a side effect outside the project tree). The customer can flip Codex on per-section in Settings.
- **Existing customers are not retroactively flipped.** The auto-apply only runs when no `resources/terminal-settings.json` file exists. Anyone with a saved settings file from 4.0.0 keeps their explicit choices.
- **Edge case (very old settings files).** If a 1.x or 2.x settings file is loaded that's missing `skillsEnabled`/`skillClients` keys entirely, `Load()` migration applies the new defaults in memory (skills appear enabled in the modal). The disk state isn't auto-applied — the next Save makes it consistent.

### Refactor

- The apply-on-save chain (MCP json/toml writers + skill installer) was extracted from `TerminalPaneViewModel` into a static `SettingsApplyHelper` so the extension's first-run path can call the same code without taking a ViewModel dependency. The orchestration layer now has unit-test coverage that didn't exist before.
```

- [ ] **Step 3: Build to verify the version change.**

```powershell
dotnet build
```

Expected: build succeeds. The `About` section in the Settings modal will show `v4.1.0` on the next deploy.

- [ ] **Step 4: Commit.**

```powershell
git add Terminal.csproj CHANGELOG.md
git commit -m "chore: release 4.1.0"
```

---

## Self-Review

**Spec coverage:**

- ✓ `TerminalSettings.Defaults()` change — Task 2.
- ✓ `SettingsApplyHelper` extraction — Task 1.
- ✓ `TryFirstRunApply` in extension — Task 3.
- ✓ `BuildFirstRunNotices` (SP-MCP probe + Maia-pane advisory) — Task 3 Step 1.
- ✓ Notice surfacing through VM — Task 4.
- ✓ Migration semantics (existing settings files untouched) — Task 2 Step 1's `Load_LegacyFileWithExplicitFalse_StaysOff` + Task 5 Step 6 manual verification.
- ✓ CHANGELOG positioning — Task 6 (above the existing 4.0.0 entry, dated 2026-05-08 to match the day's other release).
- ✓ Codex opt-in semantics — Task 2 Step 1's `Defaults_DoesNotIncludeCodex` + Task 5 Step 5 manual verification.

**Placeholder scan:** No "TBD"/"implement later"/"similar to Task N" patterns. Every code step has complete code. Every test has its setup, exercise, and assertion.

**Type consistency:**
- `SettingsApplyHelper.ApplyAll` signature: `(string projectDir, string bundledSkillsRoot, TerminalSettings prev, TerminalSettings next, Logger log, Func<int?> currentActionServerPort, Func<int?> probeStudioProMcpPort)` — used identically in Task 1 Step 4 (VM call site) and Task 3 Step 1 (extension call site).
- `Func<string[]> consumePendingFirstRunNotices` — Task 4 Step 1 (VM ctor) ↔ Task 4 Step 3 (extension call).
- `pendingFirstRunNotices: List<string>` — Task 3 Step 1 (declaration + populate) ↔ Task 4 Step 3 (extension's consume Func reads + clears).
