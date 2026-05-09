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
        settings.McpPort.Should().Be(8100);
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
            McpEnabled: true, McpPort: 7782, McpClients: new[] { "claude", "codex" },
            McpServerEnabled: false, McpServerPort: 7783,
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
    public void Load_MalformedJson_ReturnsDefaults()
    {
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"), "{ this is not json");

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.ShellPath.Should().Be(TerminalSettings.Defaults().ShellPath);
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
            McpEnabled: false, McpPort: 7782, McpClients: Array.Empty<string>(),
            McpServerEnabled: false, McpServerPort: 7783,
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
        settings.McpServerPort.Should().Be(7783);
        settings.RefreshFromDiskHotkey.Should().Be("F4");
    }

    [Fact]
    public void Save_ThenLoad_PreservesMcpServerFields()
    {
        var original = new TerminalSettings("bash.exe", new[] { "--login" }, 8192, 20000, "light",
            McpEnabled: true, McpPort: 7782, McpClients: new[] { "claude" },
            McpServerEnabled: true, McpServerPort: 7799,
            StudioProActionsEnabled: true, MaiaIntegrationEnabled: true,
            RefreshFromDiskHotkey: "Ctrl+F5", RestoreTabsOnReopen: false,
            SkillsEnabled: false, SkillClients: Array.Empty<string>());
        original.Save(tmpDir);

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.McpServerEnabled.Should().BeTrue();
        loaded.McpServerPort.Should().Be(7799);
        loaded.RefreshFromDiskHotkey.Should().Be("Ctrl+F5");
    }

    [Fact]
    public void Load_OldFileWithoutMcpServer_DefaultsToTrueOn7783F4()
    {
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"),
            """{"shellPath":"cmd.exe","mcpEnabled":false,"mcpPort":7782}""");

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.McpServerEnabled.Should().BeTrue();
        loaded.McpServerPort.Should().Be(7783);
        loaded.RefreshFromDiskHotkey.Should().Be("F4");
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
}
