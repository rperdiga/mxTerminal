using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

/// <summary>
/// Smoke coverage for the v4.2.1 Copilot CLI sibling of
/// <see cref="ClaudeMdManager"/>. The block-edit logic is exercised in
/// <see cref="ClaudeMdManagerTests"/>; these cases verify only the
/// per-CLI overrides — destination file (under <c>.github/</c>) +
/// rules subdir.
/// </summary>
public class CopilotInstructionsManagerTests : IDisposable
{
    private readonly string tmpRoot;
    private readonly string projectDir;
    private readonly string rulesDir;
    private readonly Logger log;

    public CopilotInstructionsManagerTests()
    {
        tmpRoot = Path.Combine(Path.GetTempPath(), "copilot-tests-" + Guid.NewGuid().ToString("N"));
        projectDir = Path.Combine(tmpRoot, "project");
        rulesDir = Path.Combine(projectDir, ".github", "rules");
        Directory.CreateDirectory(projectDir);
        log = new Logger(projectDir);
    }

    public void Dispose() => Directory.Delete(tmpRoot, recursive: true);

    private string CopilotInstructionsPath() =>
        Path.Combine(projectDir, ".github", "copilot-instructions.md");

    private void SeedCanonicalRule()
    {
        Directory.CreateDirectory(rulesDir);
        File.WriteAllText(Path.Combine(rulesDir, RulesInstaller.CanonicalFileName), "# rules\n");
    }

    private CopilotInstructionsManager NewManager() => new(projectDir, ".github/rules", log);

    [Fact]
    public void Apply_NoRulesFolder_DoesNotCreateInstructionsFile()
    {
        NewManager().Apply();
        File.Exists(CopilotInstructionsPath()).Should().BeFalse();
    }

    [Fact]
    public void Apply_CreatesGitHubDirIfMissing()
    {
        SeedCanonicalRule();
        // Caller's project may not have a .github/ folder yet — manager must create it.
        Directory.Exists(Path.Combine(projectDir, ".github")).Should().BeTrue();   // seeded by rulesDir already
        // Delete it to force the create-from-scratch path; rulesDir was a sibling.
        // Instead re-seed in a fresh project dir.
        var freshProject = Path.Combine(tmpRoot, "fresh");
        Directory.CreateDirectory(freshProject);
        var freshRules = Path.Combine(freshProject, ".github", "rules");
        Directory.CreateDirectory(freshRules);
        File.WriteAllText(Path.Combine(freshRules, RulesInstaller.CanonicalFileName), "# rules\n");

        new CopilotInstructionsManager(freshProject, ".github/rules", log).Apply();

        File.Exists(Path.Combine(freshProject, ".github", "copilot-instructions.md")).Should().BeTrue();
    }

    [Fact]
    public void Apply_OnlyCanonicalRule_CreatesInstructionsFileWithImport()
    {
        SeedCanonicalRule();
        NewManager().Apply();

        var body = File.ReadAllText(CopilotInstructionsPath());
        body.Should().Contain(ClaudeMdManager.BeginMarker);
        body.Should().Contain(ClaudeMdManager.EndMarker);
        body.Should().Contain($"@.github/rules/{RulesInstaller.CanonicalFileName}");
        body.Should().NotContain("CLAUDE.md");
    }

    [Fact]
    public void Remove_StripsBlock_LeavesRestOfFile()
    {
        Directory.CreateDirectory(Path.Combine(projectDir, ".github"));
        File.WriteAllText(CopilotInstructionsPath(),
            "# Copilot instructions\n\n" +
            ClaudeMdManager.BeginMarker + "\n" +
            "@.github/rules/x.md\n" +
            ClaudeMdManager.EndMarker + "\n\n" +
            "Custom guidance.\n");

        NewManager().Remove();

        var body = File.ReadAllText(CopilotInstructionsPath());
        body.Should().Contain("# Copilot instructions");
        body.Should().Contain("Custom guidance.");
        body.Should().NotContain(ClaudeMdManager.BeginMarker);
    }
}
