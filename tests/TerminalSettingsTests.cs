using FluentAssertions;
using MxStudioProTerminal;
using Xunit;

namespace MxStudioProTerminal.Tests;

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
    }

    [Fact]
    public void Save_ThenLoad_PreservesAllFields()
    {
        var original = new TerminalSettings("bash.exe", new[] { "--login" }, 8192, 20000, "light");
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
        var settings = new TerminalSettings("powershell.exe", Array.Empty<string>(), 4096, 10000, "dark");
        settings.Save(tmpDir);
        File.Exists(Path.Combine(tmpDir, "resources", "terminal-settings.json")).Should().BeTrue();
    }
}
