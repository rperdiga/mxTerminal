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
        settings.ShellPath.Should().Be("powershell.exe");
        settings.Args.Should().BeEmpty();
        settings.RingBufferKB.Should().Be(4096);
        settings.XtermScrollbackLines.Should().Be(10000);
        settings.Theme.Should().Be("dark");
        settings.McpEnabled.Should().BeFalse();
        settings.McpPort.Should().Be(7782);
        settings.McpClients.Should().BeEmpty();
    }

    [Fact]
    public void Save_ThenLoad_PreservesAllFields()
    {
        var original = new TerminalSettings("bash.exe", new[] { "--login" }, 8192, 20000, "light",
            McpEnabled: true, McpPort: 7782, McpClients: new[] { "claude", "codex" },
            ActionsServerEnabled: false, ActionsServerPort: 7783, RefreshFromDiskHotkey: "F4");
        original.Save(tmpDir);

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Load_PartialJson_FillsMissingWithDefaults()
    {
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"), """{"shellPath":"cmd.exe"}""");

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.ShellPath.Should().Be("cmd.exe");
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
        loaded.ShellPath.Should().Be("powershell.exe");
    }

    [Fact]
    public void Load_OldFileWithoutTheme_DefaultsThemeToDark()
    {
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"),
            """{"shellPath":"cmd.exe","args":[],"ringBufferKB":4096,"xtermScrollbackLines":10000}""");

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.Theme.Should().Be("dark");
    }

    [Fact]
    public void Save_CreatesResourcesDirIfMissing()
    {
        var settings = new TerminalSettings("powershell.exe", Array.Empty<string>(), 4096, 10000, "dark",
            McpEnabled: false, McpPort: 7782, McpClients: Array.Empty<string>(),
            ActionsServerEnabled: false, ActionsServerPort: 7783, RefreshFromDiskHotkey: "F4");
        settings.Save(tmpDir);
        File.Exists(Path.Combine(tmpDir, "resources", "terminal-settings.json")).Should().BeTrue();
    }

    [Fact]
    public void Load_NoFile_ActionsServerDefaults()
    {
        var settings = TerminalSettings.Load(tmpDir);
        settings.ActionsServerEnabled.Should().BeFalse();
        settings.ActionsServerPort.Should().Be(7783);
        settings.RefreshFromDiskHotkey.Should().Be("F4");
    }

    [Fact]
    public void Save_ThenLoad_PreservesActionsServerFields()
    {
        var original = new TerminalSettings("bash.exe", new[] { "--login" }, 8192, 20000, "light",
            McpEnabled: true, McpPort: 7782, McpClients: new[] { "claude" },
            ActionsServerEnabled: true, ActionsServerPort: 7799, RefreshFromDiskHotkey: "Ctrl+F5");
        original.Save(tmpDir);

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.ActionsServerEnabled.Should().BeTrue();
        loaded.ActionsServerPort.Should().Be(7799);
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
        loaded.ActionsServerEnabled.Should().BeFalse();
        loaded.ActionsServerPort.Should().Be(7783);
        loaded.RefreshFromDiskHotkey.Should().Be("F4");
    }
}
