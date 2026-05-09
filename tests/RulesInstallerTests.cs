using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class RulesInstallerTests : IDisposable
{
    private readonly string tmpRoot;
    private readonly string projectDir;
    private readonly string bundledRoot;
    private readonly Logger log;

    public RulesInstallerTests()
    {
        tmpRoot = Path.Combine(Path.GetTempPath(), "rules-installer-tests-" + Guid.NewGuid().ToString("N"));
        projectDir = Path.Combine(tmpRoot, "project");
        bundledRoot = Path.Combine(tmpRoot, "bundled");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(bundledRoot);
        log = new Logger(projectDir);
        SeedBundledRule(RulesInstaller.CanonicalFileName, "# Concord Build Rules\n\nbody v1\n");
    }

    public void Dispose() => Directory.Delete(tmpRoot, recursive: true);

    private void SeedBundledRule(string filename, string body)
    {
        File.WriteAllText(Path.Combine(bundledRoot, filename), body);
    }

    [Fact]
    public void InstallAll_CopiesCanonicalRulesFile()
    {
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");

        var dst = Path.Combine(projectDir, ".claude", "rules", RulesInstaller.CanonicalFileName);
        File.Exists(dst).Should().BeTrue();
        File.ReadAllText(dst).Should().Contain("body v1");
    }

    [Fact]
    public void InstallAll_CreatesProjectFolderWithReadmeStubOnFirstInstall()
    {
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");

        var projectFolder = Path.Combine(projectDir, ".claude", "rules", RulesInstaller.ProjectFolderName);
        Directory.Exists(projectFolder).Should().BeTrue();
        var readme = Path.Combine(projectFolder, "README.md");
        File.Exists(readme).Should().BeTrue();
        File.ReadAllText(readme).Should().Contain("Project-specific rules");
    }

    [Fact]
    public void InstallAll_IsIdempotent()
    {
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");
        installer.InstallAll(".claude/rules");

        var rulesDir = Path.Combine(projectDir, ".claude", "rules");
        Directory.EnumerateFiles(rulesDir, "*.md", SearchOption.TopDirectoryOnly)
                 .Should().HaveCount(1);
    }

    [Fact]
    public void InstallAll_RefreshesCanonicalFileOnReinstall()
    {
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");

        // Mutate the bundle and re-install.
        SeedBundledRule(RulesInstaller.CanonicalFileName, "# v2\n\nbody v2\n");
        installer.InstallAll(".claude/rules");

        var dst = Path.Combine(projectDir, ".claude", "rules", RulesInstaller.CanonicalFileName);
        File.ReadAllText(dst).Should().Contain("body v2").And.NotContain("body v1");
    }

    [Fact]
    public void InstallAll_DoesNotOverwriteUserContentInProjectFolder()
    {
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");

        // User edits the README and adds their own rule file.
        var projectFolder = Path.Combine(projectDir, ".claude", "rules", RulesInstaller.ProjectFolderName);
        var readme = Path.Combine(projectFolder, "README.md");
        File.WriteAllText(readme, "USER-EDITED README\n");
        var userRule = Path.Combine(projectFolder, "payment-conventions.md");
        File.WriteAllText(userRule, "User payment rule\n");

        // Re-install — project/ contents must be untouched.
        installer.InstallAll(".claude/rules");

        File.ReadAllText(readme).Should().Be("USER-EDITED README\n");
        File.ReadAllText(userRule).Should().Be("User payment rule\n");
    }

    [Fact]
    public void InstallAll_DoesNotRecreateReadmeIfUserDeletedIt()
    {
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");

        var projectFolder = Path.Combine(projectDir, ".claude", "rules", RulesInstaller.ProjectFolderName);
        var readme = Path.Combine(projectFolder, "README.md");
        File.Delete(readme);

        // Re-install — folder still exists, README stays deleted.
        installer.InstallAll(".claude/rules");

        Directory.Exists(projectFolder).Should().BeTrue();
        File.Exists(readme).Should().BeFalse();
    }

    [Fact]
    public void InstallAll_BundledRootMissing_NoOp()
    {
        var missingRoot = Path.Combine(tmpRoot, "no-such-bundled");
        var installer = new RulesInstaller(projectDir, missingRoot, log);
        installer.InstallAll(".claude/rules");

        Directory.Exists(Path.Combine(projectDir, ".claude")).Should().BeFalse();
    }

    [Fact]
    public void InstallAll_RemovesOrphanConcordManagedFile_OnUpgradeWhereBundleShrunk()
    {
        // Simulate v4.1.4 shipping two concord-managed rules files.
        SeedBundledRule("concord-mendix-conventions.md", "# extra v4.1.4\n");
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");

        var rulesDir = Path.Combine(projectDir, ".claude", "rules");
        File.Exists(Path.Combine(rulesDir, "concord-mendix-conventions.md")).Should().BeTrue();

        // Now simulate v4.1.5 dropping the second file from the bundle.
        File.Delete(Path.Combine(bundledRoot, "concord-mendix-conventions.md"));
        installer.InstallAll(".claude/rules");

        // Orphan must be cleaned up automatically on the next install.
        File.Exists(Path.Combine(rulesDir, "concord-mendix-conventions.md")).Should().BeFalse();
        // Canonical still installed.
        File.Exists(Path.Combine(rulesDir, RulesInstaller.CanonicalFileName)).Should().BeTrue();
    }

    [Fact]
    public void InstallAll_PreservesUserAuthoredFileWithoutConcordPrefix_OnOrphanCleanup()
    {
        // User has dropped their own file at the rules root (sibling to the
        // canonical one) — it does NOT start with the concord- prefix, so
        // orphan cleanup must not touch it.
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");

        var rulesDir = Path.Combine(projectDir, ".claude", "rules");
        var userFile = Path.Combine(rulesDir, "my-personal-rule.md");
        File.WriteAllText(userFile, "user authored\n");

        // Re-install (no bundle changes). User file must survive.
        installer.InstallAll(".claude/rules");

        File.ReadAllText(userFile).Should().Be("user authored\n");
    }

    [Fact]
    public void InstallAll_CopiesEveryTopLevelMdFromBundledRoot()
    {
        // If a future Concord ships additional canonical rule files, they all install.
        SeedBundledRule("supplementary-rules.md", "# extra\n");
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");

        var rulesDir = Path.Combine(projectDir, ".claude", "rules");
        File.Exists(Path.Combine(rulesDir, RulesInstaller.CanonicalFileName)).Should().BeTrue();
        File.Exists(Path.Combine(rulesDir, "supplementary-rules.md")).Should().BeTrue();
    }

    [Fact]
    public void RemoveAll_RemovesOnlyBundledFiles()
    {
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");

        // User adds a rule file at the rules root (sibling to the canonical one).
        var rulesRoot = Path.Combine(projectDir, ".claude", "rules");
        var userSibling = Path.Combine(rulesRoot, "my-extra-rule.md");
        File.WriteAllText(userSibling, "User extra\n");

        installer.RemoveAll(".claude/rules");

        File.Exists(Path.Combine(rulesRoot, RulesInstaller.CanonicalFileName)).Should().BeFalse();
        // User sibling survives.
        File.Exists(userSibling).Should().BeTrue();
    }

    [Fact]
    public void RemoveAll_PreservesProjectFolderAndContents()
    {
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");

        var projectFolder = Path.Combine(projectDir, ".claude", "rules", RulesInstaller.ProjectFolderName);
        var userRule = Path.Combine(projectFolder, "my-rule.md");
        File.WriteAllText(userRule, "User rule\n");

        installer.RemoveAll(".claude/rules");

        Directory.Exists(projectFolder).Should().BeTrue();
        File.Exists(userRule).Should().BeTrue();
    }

    [Fact]
    public void RemoveAll_PrunesEmptyRulesAndClaudeDirs()
    {
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");

        // Strip user-owned project folder so the rules dir CAN become empty.
        var projectFolder = Path.Combine(projectDir, ".claude", "rules", RulesInstaller.ProjectFolderName);
        Directory.Delete(projectFolder, recursive: true);

        installer.RemoveAll(".claude/rules");

        Directory.Exists(Path.Combine(projectDir, ".claude", "rules")).Should().BeFalse();
        Directory.Exists(Path.Combine(projectDir, ".claude")).Should().BeFalse();
    }

    [Fact]
    public void RemoveAll_PreservesParentDirWhenItHasOtherContent()
    {
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.InstallAll(".claude/rules");

        // Pre-existing .claude content unrelated to rules.
        var skillsDir = Path.Combine(projectDir, ".claude", "skills");
        Directory.CreateDirectory(skillsDir);
        File.WriteAllText(Path.Combine(skillsDir, "marker.txt"), "x");

        // Strip user-owned project folder so the rules dir CAN become empty.
        var projectFolder = Path.Combine(projectDir, ".claude", "rules", RulesInstaller.ProjectFolderName);
        Directory.Delete(projectFolder, recursive: true);

        installer.RemoveAll(".claude/rules");

        Directory.Exists(Path.Combine(projectDir, ".claude", "rules")).Should().BeFalse();
        Directory.Exists(skillsDir).Should().BeTrue();
        Directory.Exists(Path.Combine(projectDir, ".claude")).Should().BeTrue();
    }

    [Fact]
    public void RemoveAll_NonExistentTarget_NoOp()
    {
        var installer = new RulesInstaller(projectDir, bundledRoot, log);
        installer.RemoveAll(".claude/rules");
        Directory.Exists(Path.Combine(projectDir, ".claude")).Should().BeFalse();
    }
}
