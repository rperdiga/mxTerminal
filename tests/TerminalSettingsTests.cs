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
        // Default shell is OS-aware: powershell.exe on Windows; the user's
        // login shell or a POSIX fallback elsewhere.
        if (OperatingSystem.IsWindows())
            settings.ShellPath.Should().Be("powershell.exe");
        else
            settings.ShellPath.Should().NotBeNullOrEmpty();
        settings.Args.Should().BeEmpty();
        settings.RingBufferKB.Should().Be(4096);
        settings.XtermScrollbackLines.Should().Be(10000);
        settings.Theme.Should().Be("auto");
        settings.McpEnabled.Should().BeFalse();
        settings.McpPort.Should().Be(8100);
        settings.McpClients.Should().BeEmpty();
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
            RefreshFromDiskHotkey: "F4", RestoreTabsOnReopen: true);
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
            RefreshFromDiskHotkey: "F4", RestoreTabsOnReopen: true);
        settings.Save(tmpDir);
        File.Exists(Path.Combine(tmpDir, "resources", "terminal-settings.json")).Should().BeTrue();
    }

    [Fact]
    public void Load_NoFile_ActionsServerDefaults()
    {
        var settings = TerminalSettings.Load(tmpDir);
        settings.McpServerEnabled.Should().BeFalse();
        settings.McpServerPort.Should().Be(7783);
        settings.RefreshFromDiskHotkey.Should().Be("F4");
    }

    [Fact]
    public void Save_ThenLoad_PreservesActionsServerFields()
    {
        var original = new TerminalSettings("bash.exe", new[] { "--login" }, 8192, 20000, "light",
            McpEnabled: true, McpPort: 7782, McpClients: new[] { "claude" },
            McpServerEnabled: true, McpServerPort: 7799,
            StudioProActionsEnabled: true, MaiaIntegrationEnabled: true,
            RefreshFromDiskHotkey: "Ctrl+F5", RestoreTabsOnReopen: false);
        original.Save(tmpDir);

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.McpServerEnabled.Should().BeTrue();
        loaded.McpServerPort.Should().Be(7799);
        loaded.RefreshFromDiskHotkey.Should().Be("Ctrl+F5");
    }

    [Fact]
    public void Load_OldFileWithoutActionsServer_DefaultsToOffOn7783F4()
    {
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"),
            """{"shellPath":"cmd.exe","mcpEnabled":false,"mcpPort":7782}""");

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.McpServerEnabled.Should().BeFalse();
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
    public void Defaults_HaveSubTogglesOnAndMasterOff()
    {
        var d = TerminalSettings.Defaults();
        d.McpServerEnabled.Should().BeFalse();
        d.StudioProActionsEnabled.Should().BeTrue();
        d.MaiaIntegrationEnabled.Should().BeTrue();
    }
}
