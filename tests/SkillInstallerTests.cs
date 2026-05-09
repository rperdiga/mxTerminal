using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class SkillInstallerTests : IDisposable
{
    private readonly string tmpRoot;
    private readonly string projectDir;
    private readonly string bundledRoot;
    private readonly Logger log;

    public SkillInstallerTests()
    {
        tmpRoot = Path.Combine(Path.GetTempPath(), "skill-installer-tests-" + Guid.NewGuid().ToString("N"));
        projectDir = Path.Combine(tmpRoot, "project");
        bundledRoot = Path.Combine(tmpRoot, "bundled");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(bundledRoot);
        log = new Logger(projectDir);
        SeedBundle("alpha", "alpha body");
        SeedBundle("beta",  "beta body");
    }

    public void Dispose() => Directory.Delete(tmpRoot, recursive: true);

    private void SeedBundle(string name, string body)
    {
        var d = Path.Combine(bundledRoot, name);
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "SKILL.md"),
            $"---\nname: {name}\ndescription: bundled {name}.\n---\n{body}\n");
    }

    [Fact]
    public void InstallAll_CopiesEveryBundledSkillIntoTarget()
    {
        var installer = new SkillInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/skills");

        var alpha = Path.Combine(projectDir, ".claude", "skills", "alpha", "SKILL.md");
        var beta  = Path.Combine(projectDir, ".claude", "skills", "beta",  "SKILL.md");
        File.Exists(alpha).Should().BeTrue();
        File.Exists(beta).Should().BeTrue();
        File.ReadAllText(alpha).Should().Contain("alpha body");
    }

    [Fact]
    public void InstallAll_IsIdempotent()
    {
        var installer = new SkillInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/skills");
        installer.InstallAll(".claude/skills");  // no-throw, no duplicates
        Directory.EnumerateDirectories(Path.Combine(projectDir, ".claude", "skills"))
                 .Should().HaveCount(2);
    }

    [Fact]
    public void InstallAll_OverwritesExistingFilesInBundledFolders()
    {
        var installer = new SkillInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/skills");

        // Mutate the bundle and re-install — output should match new content.
        SeedBundle("alpha", "alpha NEW body");
        installer.InstallAll(".claude/skills");

        var alpha = Path.Combine(projectDir, ".claude", "skills", "alpha", "SKILL.md");
        File.ReadAllText(alpha).Should().Contain("alpha NEW body").And.NotContain("alpha body\n");
    }

    [Fact]
    public void InstallAll_CopiesSubFilesInsideSkillFolder()
    {
        // Bundled skill with extra files (templates, references).
        var refsDir = Path.Combine(bundledRoot, "alpha", "references");
        Directory.CreateDirectory(refsDir);
        File.WriteAllText(Path.Combine(refsDir, "extra.md"), "extra content\n");

        var installer = new SkillInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/skills");

        var copied = Path.Combine(projectDir, ".claude", "skills", "alpha", "references", "extra.md");
        File.Exists(copied).Should().BeTrue();
        File.ReadAllText(copied).Should().Be("extra content\n");
    }

    [Fact]
    public void InstallAll_OverlayReplacesPrimarySkill()
    {
        // Seed an overlay with one skill that shares a name with a primary skill.
        var overlayRoot = Path.Combine(tmpRoot, "overlay");
        Directory.CreateDirectory(overlayRoot);
        var overlayAlpha = Path.Combine(overlayRoot, "alpha");
        Directory.CreateDirectory(overlayAlpha);
        File.WriteAllText(Path.Combine(overlayAlpha, "SKILL.md"),
            "---\nname: alpha\ndescription: overlay alpha.\n---\nalpha OVERLAY body\n");

        var installer = new SkillInstaller(projectDir, bundledRoot, overlayAlpha is null ? null : overlayRoot, log);
        installer.InstallAll(".claude/skills");

        var alphaPath = Path.Combine(projectDir, ".claude", "skills", "alpha", "SKILL.md");
        var betaPath  = Path.Combine(projectDir, ".claude", "skills", "beta",  "SKILL.md");

        // Overlay wins for matching names; non-matching primary skill is unaffected.
        File.ReadAllText(alphaPath).Should().Contain("alpha OVERLAY body").And.NotContain("alpha body\n");
        File.ReadAllText(betaPath).Should().Contain("beta body");
    }

    [Fact]
    public void InstallAll_OverlayMissingDoesNotThrow()
    {
        // Configure an overlay path that doesn't exist on disk — installer should
        // ignore it and behave like the no-overlay case.
        var overlayRoot = Path.Combine(tmpRoot, "no-such-overlay");
        var installer = new SkillInstaller(projectDir, bundledRoot, overlayRoot, log);

        installer.InstallAll(".claude/skills");

        var alpha = Path.Combine(projectDir, ".claude", "skills", "alpha", "SKILL.md");
        File.ReadAllText(alpha).Should().Contain("alpha body");
    }

    [Fact]
    public void RemoveAll_RemovesOnlyBundledFolders()
    {
        var installer = new SkillInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/skills");

        // User-authored skill, NOT bundled.
        var userSkill = Path.Combine(projectDir, ".claude", "skills", "my-thing");
        Directory.CreateDirectory(userSkill);
        File.WriteAllText(Path.Combine(userSkill, "SKILL.md"), "user content\n");

        installer.RemoveAll(".claude/skills");

        Directory.Exists(Path.Combine(projectDir, ".claude", "skills", "alpha")).Should().BeFalse();
        Directory.Exists(Path.Combine(projectDir, ".claude", "skills", "beta")).Should().BeFalse();
        // User skill survives.
        File.Exists(Path.Combine(userSkill, "SKILL.md")).Should().BeTrue();
    }

    [Fact]
    public void RemoveAll_RemovesEmptySkillsDirAndEmptyParent()
    {
        var installer = new SkillInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/skills");
        installer.RemoveAll(".claude/skills");

        Directory.Exists(Path.Combine(projectDir, ".claude", "skills")).Should().BeFalse();
        Directory.Exists(Path.Combine(projectDir, ".claude")).Should().BeFalse();
    }

    [Fact]
    public void RemoveAll_PreservesParentDirWhenItHasOtherContent()
    {
        // Pre-existing .github folder with content unrelated to skills.
        var gh = Path.Combine(projectDir, ".github");
        Directory.CreateDirectory(gh);
        File.WriteAllText(Path.Combine(gh, "CODEOWNERS"), "* @team\n");

        var installer = new SkillInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".github/skills");
        installer.RemoveAll(".github/skills");

        Directory.Exists(Path.Combine(gh, "skills")).Should().BeFalse();
        Directory.Exists(gh).Should().BeTrue();
        File.Exists(Path.Combine(gh, "CODEOWNERS")).Should().BeTrue();
    }

    [Fact]
    public void RemoveAll_PreservesUserSkillsDirWhenUserSkillsRemain()
    {
        var installer = new SkillInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/skills");

        var userSkill = Path.Combine(projectDir, ".claude", "skills", "my-thing");
        Directory.CreateDirectory(userSkill);
        File.WriteAllText(Path.Combine(userSkill, "SKILL.md"), "user\n");

        installer.RemoveAll(".claude/skills");

        Directory.Exists(Path.Combine(projectDir, ".claude", "skills")).Should().BeTrue();
        Directory.Exists(Path.Combine(projectDir, ".claude")).Should().BeTrue();
    }

    [Fact]
    public void RemoveAll_NonExistentTarget_NoOp()
    {
        var installer = new SkillInstaller(projectDir, bundledRoot, log);
        // Never installed — RemoveAll should not throw and should not create dirs.
        installer.RemoveAll(".claude/skills");
        Directory.Exists(Path.Combine(projectDir, ".claude")).Should().BeFalse();
    }
}
