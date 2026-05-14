using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class SettingsApplyHelperTests : IDisposable
{
    private readonly string tmpRoot;
    private readonly string projectDir;
    private readonly string bundledRoot;
    private readonly string bundledRulesRoot;
    private readonly Logger log;

    public SettingsApplyHelperTests()
    {
        tmpRoot = Path.Combine(Path.GetTempPath(), "settings-apply-tests-" + Guid.NewGuid().ToString("N"));
        projectDir = Path.Combine(tmpRoot, "project");
        bundledRoot = Path.Combine(tmpRoot, "bundled");
        bundledRulesRoot = Path.Combine(tmpRoot, "bundled-rules");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(bundledRoot);
        Directory.CreateDirectory(bundledRulesRoot);
        log = new Logger(projectDir);
        SeedBundledSkill("alpha");
        SeedBundledSkill("beta");
        File.WriteAllText(Path.Combine(bundledRulesRoot, "concord-build-rules.md"), "# bundled rules\n");
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
            projectDir, bundledRoot, bundledRulesRoot, s, s, log,
            currentActionServerPort: () => null,
            probeStudioProMcpPort:      () => null,
            probeStudioProMcpAvailable: () => true);
        touched.Should().BeEmpty();
    }

    [Fact]
    public void ApplyAll_NoneToAll_WritesMcpJsonAndInstallsSkills()
    {
        var prev = AllOff();
        var next = ClaudePlusCopilot();

        var touched = SettingsApplyHelper.ApplyAll(
            projectDir, bundledRoot, bundledRulesRoot, prev, next, log,
            currentActionServerPort: () => 7783,
            probeStudioProMcpPort:      () => 8100,
            probeStudioProMcpAvailable: () => true);

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
        SettingsApplyHelper.ApplyAll(projectDir, bundledRoot, bundledRulesRoot, AllOff(), prev, log,
            currentActionServerPort: () => 7783, probeStudioProMcpPort: () => 8100,
            probeStudioProMcpAvailable: () => true);
        File.Exists(Path.Combine(projectDir, ".mcp.json")).Should().BeTrue();

        var touched = SettingsApplyHelper.ApplyAll(
            projectDir, bundledRoot, bundledRulesRoot, prev, next, log,
            currentActionServerPort: () => 7783,
            probeStudioProMcpPort:      () => 8100,
            probeStudioProMcpAvailable: () => true);

        // .mcp.json should be removed entirely (no other servers in it).
        File.Exists(Path.Combine(projectDir, ".mcp.json")).Should().BeFalse();
        Directory.Exists(Path.Combine(projectDir, ".claude", "skills", "alpha")).Should().BeFalse();
        Directory.Exists(Path.Combine(projectDir, ".github", "skills", "alpha")).Should().BeFalse();
        touched.Should().NotBeEmpty();
    }

    [Fact]
    public void ApplyAll_ProbedPortFallsBackToConstant_WhenProbeReturnsNull()
    {
        // v4.1.2: McpPort field was removed from TerminalSettings (it had
        // become dead-weight that mis-displayed runtime state as user intent).
        // The probe-failure fallback is now a documented constant, not a
        // round-trippable saved value.
        var prev = AllOff();
        var next = ClaudePlusCopilot();

        SettingsApplyHelper.ApplyAll(
            projectDir, bundledRoot, bundledRulesRoot, prev, next, log,
            currentActionServerPort: () => 7783,
            probeStudioProMcpPort:      () => null,  // probe fails
            probeStudioProMcpAvailable: () => true);

        var json = File.ReadAllText(Path.Combine(projectDir, ".mcp.json"));
        // Falls back to TerminalSettings.StudioProMcpDefaultPort = 8100.
        json.Should().Contain(TerminalSettings.StudioProMcpDefaultPort.ToString());
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
            projectDir, bundledRoot, bundledRulesRoot, prev, next, log,
            currentActionServerPort: () => 7783,
            probeStudioProMcpPort:      () => 8100,
            probeStudioProMcpAvailable: () => true);

        File.Exists(Path.Combine(projectDir, ".mcp.json")).Should().BeTrue();
        Directory.Exists(Path.Combine(projectDir, ".claude", "skills")).Should().BeFalse();
        touched.Should().Contain(t => t.Contains("Claude"));
        touched.Should().NotContain(t => t.Contains("skills"));
    }

    [Fact]
    public void ApplyAll_McpUnavailable_SkipsUpsertAndPreventsWiring()
    {
        // Studio Pro 10.x / 11.6–11.9: probe returns Available=false.
        // Even when next.McpEnabled=true and McpClients includes Claude/Copilot,
        // the apply MUST NOT write a mendix-studio-pro entry.
        var prev = AllOff();
        var next = ClaudePlusCopilot();  // wants mendix-studio-pro wired

        var touched = SettingsApplyHelper.ApplyAll(
            projectDir, bundledRoot, bundledRulesRoot, prev, next, log,
            currentActionServerPort: () => 7783,
            probeStudioProMcpPort:      () => 8100,
            probeStudioProMcpAvailable: () => false);  // <-- the gate

        if (File.Exists(Path.Combine(projectDir, ".mcp.json")))
        {
            var json = File.ReadAllText(Path.Combine(projectDir, ".mcp.json"));
            json.Should().NotContain("mendix-studio-pro",
                "Studio Pro 10.x / pre-11.10 has no mendix-studio-pro MCP server");
        }
        // The Concord MCP entry (concord-mcp) is unrelated to the availability
        // gate and should still wire normally.
        touched.Should().Contain(t => t.Contains("actions"));
    }

    [Fact]
    public void ApplyAll_McpUnavailable_CleansUpStaleMendixStudioProEntry()
    {
        // Cross-version migration: project was last opened on 11.10+ (so
        // .mcp.json has a stale mendix-studio-pro entry), now opens on 10.x.
        // The apply must REMOVE the orphaned entry so it doesn't dangle.
        var prev = ClaudePlusCopilot();  // had it wired
        // Pre-populate the project as if from a prior 11.10+ apply.
        SettingsApplyHelper.ApplyAll(projectDir, bundledRoot, bundledRulesRoot, AllOff(), prev, log,
            currentActionServerPort: () => 7783, probeStudioProMcpPort: () => 8100,
            probeStudioProMcpAvailable: () => true);
        File.ReadAllText(Path.Combine(projectDir, ".mcp.json")).Should().Contain("mendix-studio-pro");

        // Now the same project opens on Studio Pro 10.x. Even though saved
        // settings still say McpEnabled=true, the apply must clean up.
        var touched = SettingsApplyHelper.ApplyAll(
            projectDir, bundledRoot, bundledRulesRoot, prev, prev, log,
            currentActionServerPort: () => 7783,
            probeStudioProMcpPort:      () => 8100,
            probeStudioProMcpAvailable: () => false);

        if (File.Exists(Path.Combine(projectDir, ".mcp.json")))
        {
            var json = File.ReadAllText(Path.Combine(projectDir, ".mcp.json"));
            json.Should().NotContain("mendix-studio-pro",
                "stale mendix-studio-pro from prior 11.10+ open must be cleaned up");
        }
    }
}
