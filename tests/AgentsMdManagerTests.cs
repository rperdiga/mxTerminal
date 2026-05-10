using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

/// <summary>
/// Smoke coverage for the v4.2.1 Codex sibling of <see cref="ClaudeMdManager"/>.
/// The block-edit logic is tested exhaustively in <see cref="ClaudeMdManagerTests"/>;
/// these cases verify only the per-CLI overrides — destination file +
/// rules subdir — actually take effect.
/// </summary>
public class AgentsMdManagerTests : IDisposable
{
    private readonly string tmpRoot;
    private readonly string projectDir;
    private readonly string rulesDir;
    private readonly Logger log;

    public AgentsMdManagerTests()
    {
        tmpRoot = Path.Combine(Path.GetTempPath(), "agents-md-tests-" + Guid.NewGuid().ToString("N"));
        projectDir = Path.Combine(tmpRoot, "project");
        rulesDir = Path.Combine(projectDir, ".codex", "rules");
        Directory.CreateDirectory(projectDir);
        log = new Logger(projectDir);
    }

    public void Dispose() => Directory.Delete(tmpRoot, recursive: true);

    private string AgentsMdPath() => Path.Combine(projectDir, AgentsMdManager.AgentsMdFileName);

    private void SeedCanonicalRule()
    {
        Directory.CreateDirectory(rulesDir);
        File.WriteAllText(Path.Combine(rulesDir, RulesInstaller.CanonicalFileName), "# rules\n");
    }

    private AgentsMdManager NewManager() => new(projectDir, ".codex/rules", log);

    [Fact]
    public void Apply_NoRulesFolder_DoesNotCreateAgentsMd()
    {
        NewManager().Apply();
        File.Exists(AgentsMdPath()).Should().BeFalse();
    }

    [Fact]
    public void Apply_OnlyCanonicalRule_CreatesAgentsMdWithImport()
    {
        SeedCanonicalRule();
        NewManager().Apply();

        var body = File.ReadAllText(AgentsMdPath());
        body.Should().Contain(ClaudeMdManager.BeginMarker);
        body.Should().Contain(ClaudeMdManager.EndMarker);
        body.Should().Contain($"@.codex/rules/{RulesInstaller.CanonicalFileName}");
        body.Should().NotContain("CLAUDE.md");
        body.Should().NotContain(".claude/rules");
    }

    [Fact]
    public void Apply_PreservesUserContent_AboveAndBelowBlock()
    {
        SeedCanonicalRule();
        File.WriteAllText(AgentsMdPath(),
            "# My Codex agents\n\n" +
            "Some user-authored content above.\n\n" +
            ClaudeMdManager.BeginMarker + "\n" +
            "@.codex/rules/old.md\n" +
            ClaudeMdManager.EndMarker + "\n\n" +
            "User content below.\n");

        NewManager().Apply();

        var body = File.ReadAllText(AgentsMdPath());
        body.Should().Contain("# My Codex agents");
        body.Should().Contain("Some user-authored content above.");
        body.Should().Contain("User content below.");
        body.Should().Contain($"@.codex/rules/{RulesInstaller.CanonicalFileName}");
    }

    [Fact]
    public void Remove_StripsBlock_LeavesRestOfFile()
    {
        File.WriteAllText(AgentsMdPath(),
            "# Notes\n\n" +
            ClaudeMdManager.BeginMarker + "\n" +
            "@.codex/rules/x.md\n" +
            ClaudeMdManager.EndMarker + "\n\n" +
            "Tail.\n");

        NewManager().Remove();

        var body = File.ReadAllText(AgentsMdPath());
        body.Should().Contain("# Notes");
        body.Should().Contain("Tail.");
        body.Should().NotContain(ClaudeMdManager.BeginMarker);
    }
}
