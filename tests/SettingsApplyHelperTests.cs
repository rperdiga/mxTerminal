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
