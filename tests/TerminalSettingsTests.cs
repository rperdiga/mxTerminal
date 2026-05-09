using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class TerminalSettingsTests : IDisposable
{
    private readonly string tmpDir;

    public TerminalSettingsTests()
    {
        tmpDir = Path.Combine(Path.GetTempPath(), "mxterm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
    }

    public void Dispose() => Directory.Delete(tmpDir, recursive: true);

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
        settings.McpClients.Should().BeEquivalentTo(new[] { "claude", "copilot" });
        settings.McpServerEnabled.Should().BeTrue();
        settings.StudioProActionsEnabled.Should().BeTrue();
        settings.MaiaIntegrationEnabled.Should().BeTrue();
        settings.SkillsEnabled.Should().BeTrue();
        settings.SkillClients.Should().BeEquivalentTo(new[] { "claude", "copilot" });
    }

    [Fact]
    public void Save_ThenLoad_PreservesAllFields()
    {
        // Use a shell path appropriate for the current OS so the platform-
        // migration logic in TerminalSettings.Load doesn't rewrite it.
        var shell = OperatingSystem.IsWindows() ? "bash.exe" : "/bin/bash";
        var original = new TerminalSettings(shell, new[] { "--login" }, 8192, 20000, "light",
            McpEnabled: true, McpClients: new[] { "claude", "codex" },
            McpServerEnabled: false,
            StudioProActionsEnabled: true, MaiaIntegrationEnabled: true,
            RefreshFromDiskHotkey: "F4", RestoreTabsOnReopen: true,
            SkillsEnabled: false, SkillClients: Array.Empty<string>());
        original.Save(tmpDir);

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Load_PartialJson_FillsMissingWithDefaults()
    {
        // Use a path that survives the platform-migration check (a bare command
        // name without path separator or .exe suffix passes through on any OS).
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"), """{"shellPath":"customshell"}""");

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.ShellPath.Should().Be("customshell");
        loaded.Args.Should().BeEmpty();
        loaded.RingBufferKB.Should().Be(4096);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaults_AndBacksUpBrokenFile()
    {
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        var settingsPath = Path.Combine(resourcesDir, "terminal-settings.json");
        File.WriteAllText(settingsPath, "{ this is not json");

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.ShellPath.Should().Be(TerminalSettings.Defaults().ShellPath);

        // Original broken file moved aside so the user can recover by hand.
        File.Exists(settingsPath).Should().BeFalse();
        Directory.GetFiles(resourcesDir, "terminal-settings.json.broken-*.bak")
            .Should().HaveCount(1);
    }

    [Fact]
    public void Load_OldFileWithoutTheme_DefaultsThemeToAuto()
    {
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"),
            """{"shellPath":"cmd.exe","args":[],"ringBufferKB":4096,"xtermScrollbackLines":10000}""");

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.Theme.Should().Be("auto");
    }

    [Fact]
    public void Save_CreatesResourcesDirIfMissing()
    {
        var settings = new TerminalSettings("powershell.exe", Array.Empty<string>(), 4096, 10000, "dark",
            McpEnabled: false, McpClients: Array.Empty<string>(),
            McpServerEnabled: false,
            StudioProActionsEnabled: true, MaiaIntegrationEnabled: true,
            RefreshFromDiskHotkey: "F4", RestoreTabsOnReopen: true,
            SkillsEnabled: false, SkillClients: Array.Empty<string>());
        settings.Save(tmpDir);
        File.Exists(Path.Combine(tmpDir, "resources", "terminal-settings.json")).Should().BeTrue();
    }

    [Fact]
    public void Load_NoFile_McpServerDefaults()
    {
        var settings = TerminalSettings.Load(tmpDir);
        settings.McpServerEnabled.Should().BeTrue();
        settings.RefreshFromDiskHotkey.Should().Be("F4");
    }

    [Fact]
    public void Save_ThenLoad_PreservesMcpServerEnabled()
    {
        var original = new TerminalSettings("bash.exe", new[] { "--login" }, 8192, 20000, "light",
            McpEnabled: true, McpClients: new[] { "claude" },
            McpServerEnabled: true,
            StudioProActionsEnabled: true, MaiaIntegrationEnabled: true,
            RefreshFromDiskHotkey: "Ctrl+F5", RestoreTabsOnReopen: false,
            SkillsEnabled: false, SkillClients: Array.Empty<string>());
        original.Save(tmpDir);

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.McpServerEnabled.Should().BeTrue();
        loaded.RefreshFromDiskHotkey.Should().Be("Ctrl+F5");
    }

    [Fact]
    public void Load_OldFileWithoutMcpServer_DefaultsMasterTrueAndF4Hotkey()
    {
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"),
            """{"shellPath":"cmd.exe","mcpEnabled":false}""");

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.McpServerEnabled.Should().BeTrue();
        loaded.RefreshFromDiskHotkey.Should().Be("F4");
    }

    [Fact]
    public void Load_OldFileWithStalePortKeys_IgnoresThemSilently()
    {
        // Pre-4.1.2 settings files persist mcpServerPort, mcpPort, and the
        // legacy actionsServerPort. After 4.1.2 these fields don't exist on
        // TerminalSettings — the runtime probes/picks ports itself. Confirm
        // an old file deserializes fine with the stale keys ignored.
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"), """
            {
              "shellPath": "powershell.exe",
              "mcpEnabled": true,
              "mcpPort": 8100,
              "mcpServerPort": 8099,
              "actionsServerPort": 8099,
              "mcpServerEnabled": true
            }
            """);

        // Should not throw.
        var loaded = TerminalSettings.Load(tmpDir);
        loaded.McpEnabled.Should().BeTrue();
        loaded.McpServerEnabled.Should().BeTrue();
    }

    [Fact]
    public void Save_DoesNotEmitRemovedPortKeys()
    {
        // Belt-and-braces guard against the v4.1.x port-leak coming back:
        // assert that a fresh Save never emits the removed port keys.
        TerminalSettings.Defaults().Save(tmpDir);
        var json = File.ReadAllText(Path.Combine(tmpDir, "resources", "terminal-settings.json"));
        json.Should().NotContain("mcpPort");
        json.Should().NotContain("mcpServerPort");
        json.Should().NotContain("actionsServerPort");
    }

    [Fact]
    public void Load_OldSchemaWithActionsServerEnabled_MigratesToMcpServerEnabled()
    {
        var dir = Directory.CreateTempSubdirectory("concord-settings-").FullName;
        try
        {
            var resDir = Path.Combine(dir, "resources");
            Directory.CreateDirectory(resDir);
            // Old schema: actionsServerEnabled present, McpServerEnabled absent.
            File.WriteAllText(Path.Combine(resDir, "terminal-settings.json"),
                """{"shellPath":"powershell.exe","actionsServerEnabled":true}""");

            var s = TerminalSettings.Load(dir);

            s.McpServerEnabled.Should().BeTrue();
            s.StudioProActionsEnabled.Should().BeTrue();
            s.MaiaIntegrationEnabled.Should().BeTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Defaults_HaveAllTogglesOnExceptCodex()
    {
        var d = TerminalSettings.Defaults();
        d.McpEnabled.Should().BeTrue();
        d.McpServerEnabled.Should().BeTrue();
        d.StudioProActionsEnabled.Should().BeTrue();
        d.MaiaIntegrationEnabled.Should().BeTrue();
        d.SkillsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Load_NoFile_AllOnExceptCodex()
    {
        var settings = TerminalSettings.Load(tmpDir);
        settings.SkillsEnabled.Should().BeTrue();
        settings.SkillClients.Should().BeEquivalentTo(new[] { "claude", "copilot" });
        settings.SkillClients.Should().NotContain("codex");
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
    public void Save_ThenLoad_PreservesSkillFields()
    {
        var defaults = TerminalSettings.Defaults();
        var saved = defaults with
        {
            SkillsEnabled = true,
            SkillClients = new[] { "claude", "codex" },
        };
        saved.Save(tmpDir);

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.SkillsEnabled.Should().BeTrue();
        loaded.SkillClients.Should().BeEquivalentTo(new[] { "claude", "codex" });
    }

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
    public void Defaults_DoesNotIncludeCodex()
    {
        var d = TerminalSettings.Defaults();
        d.McpClients.Should().NotContain("codex");
        d.SkillClients.Should().NotContain("codex");
    }

    [Fact]
    public void Defaults_LastAppliedVersion_IsNull()
    {
        TerminalSettings.Defaults().LastAppliedVersion.Should().BeNull();
    }

    [Fact]
    public void Save_ThenLoad_PreservesLastAppliedVersion()
    {
        var stamped = TerminalSettings.Defaults() with { LastAppliedVersion = "4.1.0" };
        stamped.Save(tmpDir);

        TerminalSettings.Load(tmpDir).LastAppliedVersion.Should().Be("4.1.0");
    }

    [Fact]
    public void Load_FileWithoutVersionStamp_ReturnsNull()
    {
        // A pre-tracking settings file (anything written by Concord <= 4.1.0
        // before the LastAppliedVersion field existed) has no
        // lastAppliedVersion key. Load returns null so TryUpgradeApply can
        // recognize the file as needing the apply pass.
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"),
            """{"shellPath":"powershell.exe","mcpEnabled":true}""");

        TerminalSettings.Load(tmpDir).LastAppliedVersion.Should().BeNull();
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsNullStamp()
    {
        // Defaults() leaves LastAppliedVersion null. Round-trip must
        // preserve that — the apply paths are the only places that
        // populate the stamp.
        TerminalSettings.Defaults().Save(tmpDir);
        TerminalSettings.Load(tmpDir).LastAppliedVersion.Should().BeNull();
    }
}

public class UpgradeApplyDecisionTests
{
    [Theory]
    [InlineData(null,    "4.1.0", true)]   // never stamped → apply
    [InlineData("",      "4.1.0", true)]   // empty stamp → apply
    [InlineData("1.1.1", "4.1.0", true)]   // older → apply
    [InlineData("4.0.0", "4.1.0", true)]   // older minor → apply
    [InlineData("4.1.0", "4.1.0", false)]  // equal → no-op (already applied)
    [InlineData("4.2.0", "4.1.0", false)]  // newer (colleague pulled) → no-op
    [InlineData("999.0.0", "4.1.0", false)]// far newer → no-op
    [InlineData("not-a-version", "4.1.0", true)]  // unparsable + mismatch → apply
    [InlineData("not-a-version", "not-a-version", false)] // unparsable + match → no-op
    public void IsUpgradeApplyNeeded(string? stamp, string current, bool expected)
    {
        TerminalPaneExtension.IsUpgradeApplyNeeded(stamp, current).Should().Be(expected);
    }
}
