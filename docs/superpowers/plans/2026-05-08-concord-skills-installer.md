# Concord Skills Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the "Coming soon" placeholder in the Skills section of the Concord settings modal with a working bundled-skills installer that writes Mendix skill packs into the open project's `.claude/skills/`, `.codex/skills/`, and `.github/skills/` directories per the user's per-CLI selection.

**Architecture:** Mirror the existing MCP wiring pattern. Concord ships 7 skill folders bundled at `extensions/Concord/skills/`. A new `SkillInstaller` class installs/uninstalls them per CLI. `TerminalSettings` gains `SkillsEnabled` + `SkillClients` fields. `TerminalPaneViewModel.HandleSaveSettings` gains an `ApplySkillsConfig` branch that diffs prev/next and calls the installer. The Skills section of the settings modal gets a master toggle + per-CLI checkboxes + a read-only list of bundled skill names.

**Tech Stack:** C# 8, .NET 8, xUnit + FluentAssertions for tests, MEF (`[Export]` / `[ImportingConstructor]`) for DI, Mendix `IExtensionFileService` for resolving the bundled-skills path, plain TypeScript for the modal UI.

**Spec:** [docs/superpowers/specs/2026-05-08-concord-skills-installer-design.md](../specs/2026-05-08-concord-skills-installer-design.md)

---

## Task 1: Bundle the 7 Mendix skills into the Concord repo

**Files:**
- Create: `skills/mendix-microflow-update/SKILL.md`
- Create: `skills/mendix-microflow-syntax/SKILL.md`
- Create: `skills/mendix-microflow-common/SKILL.md`
- Create: `skills/mendix-page-gen/SKILL.md`
- Create: `skills/mendix-view-entities/SKILL.md`
- Create: `skills/mendix-workflow-update/SKILL.md`
- Create: `skills/mendix-workflow-common/SKILL.md`
- Modify: `Terminal.csproj:54-61`

**Source of truth:** `C:\Projects\AltairTraversalViewer\.claude\skills\` is the donor. After this task the repo is the canonical source — the AltairTraversalViewer copy is no longer authoritative.

- [ ] **Step 1: Copy the 7 skill folders into `skills/` at the repo root.**

PowerShell (run from `c:\Extensions\Terminal`):

```powershell
$src = "C:\Projects\AltairTraversalViewer\.claude\skills"
$dst = "skills"
New-Item -ItemType Directory -Path $dst -Force | Out-Null
Copy-Item "$src\mendix-microflow-update"  -Destination $dst -Recurse -Force
Copy-Item "$src\mendix-microflow-syntax"  -Destination $dst -Recurse -Force
Copy-Item "$src\mendix-microflow-common"  -Destination $dst -Recurse -Force
Copy-Item "$src\mendix-page-gen"          -Destination $dst -Recurse -Force
Copy-Item "$src\mendix-view-entities"     -Destination $dst -Recurse -Force
Copy-Item "$src\mendix-workflow-update"   -Destination $dst -Recurse -Force
Copy-Item "$src\mendix-workflow-common"   -Destination $dst -Recurse -Force
```

Expected: `skills/<name>/SKILL.md` exists for each of the 7 names. Some folders may also contain supplemental files (templates, references) — those are copied along by `-Recurse`.

- [ ] **Step 2: Verify each `SKILL.md` has frontmatter with `name:` and `description:`.**

Run:

```powershell
Get-ChildItem skills -Recurse -Filter SKILL.md | ForEach-Object {
  $first = (Get-Content $_.FullName -TotalCount 6) -join "`n"
  if ($first -notmatch "(?m)^name:" -or $first -notmatch "(?m)^description:") {
    Write-Error "Bad frontmatter: $($_.FullName)"
  } else {
    Write-Host "OK: $($_.FullName)"
  }
}
```

Expected: 7 lines of "OK: ...". If any "Bad frontmatter" line appears, hand-fix that file's frontmatter before continuing.

- [ ] **Step 3: Add `skills/**/*` to `Terminal.csproj`'s content-copy block.**

Open `Terminal.csproj` and replace the `<ItemGroup>` block at lines 54-61:

```xml
  <ItemGroup>
    <Content Include="manifest.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content>
    <Content Include="wwwroot/**/*">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
      <Visible>false</Visible>
    </Content>
    <EmbeddedResource Include="src/Maia/maia_agent.js" LogicalName="Terminal.Maia.maia_agent.js" />
  </ItemGroup>
```

with:

```xml
  <ItemGroup>
    <Content Include="manifest.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content>
    <Content Include="wwwroot/**/*">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
      <Visible>false</Visible>
    </Content>
    <Content Include="skills/**/*">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
      <Visible>false</Visible>
    </Content>
    <EmbeddedResource Include="src/Maia/maia_agent.js" LogicalName="Terminal.Maia.maia_agent.js" />
  </ItemGroup>
```

- [ ] **Step 4: Build to verify deployment.**

Run:

```powershell
dotnet build
```

Expected: build succeeds. The output directory (`bin\Debug\net8.0\skills\`) contains the 7 skill folders. If `MendixDeployTarget` is set in `Directory.Build.props`, the target's `extensions\Concord\skills\` directory also has them.

Verify:

```powershell
Test-Path bin\Debug\net8.0\skills\mendix-microflow-update\SKILL.md
```

Expected: `True`.

- [ ] **Step 5: Commit.**

```powershell
git add skills Terminal.csproj
git commit -m "feat(skills): bundle 7 Mendix skill packs into extension"
```

---

## Task 2: `BundledSkillReader` — enumerate bundled skills and parse frontmatter

**Files:**
- Create: `src/BundledSkillReader.cs`
- Test: `tests/BundledSkillReaderTests.cs`

**Why:** The settings modal needs to show users which skills are bundled. The `BuildSettingsPayload` server-side enumeration calls this reader; the JS side just renders. Keeping the reader separate makes it unit-testable without spinning up a `TerminalPaneViewModel`.

- [ ] **Step 1: Write the failing test.**

Create `tests/BundledSkillReaderTests.cs`:

```csharp
using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class BundledSkillReaderTests : IDisposable
{
    private readonly string tmpDir;

    public BundledSkillReaderTests()
    {
        tmpDir = Path.Combine(Path.GetTempPath(), "skill-reader-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
    }

    public void Dispose() => Directory.Delete(tmpDir, recursive: true);

    [Fact]
    public void Enumerate_NonExistentRoot_ReturnsEmpty()
    {
        var missing = Path.Combine(tmpDir, "nope");
        BundledSkillReader.Enumerate(missing).Should().BeEmpty();
    }

    [Fact]
    public void Enumerate_ParsesNameAndDescriptionFromFrontmatter()
    {
        var skillDir = Path.Combine(tmpDir, "alpha");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name: alpha
            description: First skill for testing.
            ---

            # Alpha
            Body text.
            """);

        var skills = BundledSkillReader.Enumerate(tmpDir);
        skills.Should().HaveCount(1);
        skills[0].Name.Should().Be("alpha");
        skills[0].Description.Should().Be("First skill for testing.");
    }

    [Fact]
    public void Enumerate_FallsBackToFolderNameWhenFrontmatterMissingName()
    {
        var skillDir = Path.Combine(tmpDir, "beta");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            description: No name in frontmatter.
            ---
            Body.
            """);

        var skills = BundledSkillReader.Enumerate(tmpDir);
        skills[0].Name.Should().Be("beta");
        skills[0].Description.Should().Be("No name in frontmatter.");
    }

    [Fact]
    public void Enumerate_SkipsFoldersWithoutSkillMd()
    {
        Directory.CreateDirectory(Path.Combine(tmpDir, "gamma"));
        // gamma has no SKILL.md — it should be ignored.
        var deltaDir = Path.Combine(tmpDir, "delta");
        Directory.CreateDirectory(deltaDir);
        File.WriteAllText(Path.Combine(deltaDir, "SKILL.md"), """
            ---
            name: delta
            description: Has SKILL.md.
            ---
            """);

        var skills = BundledSkillReader.Enumerate(tmpDir);
        skills.Should().HaveCount(1);
        skills[0].Name.Should().Be("delta");
    }

    [Fact]
    public void Enumerate_HandlesMissingFrontmatterGracefully()
    {
        var skillDir = Path.Combine(tmpDir, "epsilon");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "No frontmatter here.\n");

        var skills = BundledSkillReader.Enumerate(tmpDir);
        skills[0].Name.Should().Be("epsilon");
        skills[0].Description.Should().Be("");
    }

    [Fact]
    public void Enumerate_ReturnsSkillsSortedByName()
    {
        foreach (var n in new[] { "zeta", "alpha", "delta" })
        {
            var d = Path.Combine(tmpDir, n);
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "SKILL.md"), $"---\nname: {n}\ndescription: x\n---\n");
        }
        var names = BundledSkillReader.Enumerate(tmpDir).Select(s => s.Name).ToArray();
        names.Should().Equal("alpha", "delta", "zeta");
    }

    [Fact]
    public void Enumerate_StripsLeadingAndTrailingWhitespaceFromValues()
    {
        var skillDir = Path.Combine(tmpDir, "eta");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name:    eta
            description:    Padded value.
            ---
            """);
        var s = BundledSkillReader.Enumerate(tmpDir).Single();
        s.Name.Should().Be("eta");
        s.Description.Should().Be("Padded value.");
    }
}
```

- [ ] **Step 2: Run the failing tests.**

```powershell
dotnet test --filter FullyQualifiedName~BundledSkillReaderTests
```

Expected: build error — `BundledSkillReader` does not exist.

- [ ] **Step 3: Implement `BundledSkillReader`.**

Create `src/BundledSkillReader.cs`:

```csharp
namespace Terminal;

public sealed record BundledSkillInfo(string Name, string Description);

/// <summary>
/// Enumerates the bundled-skills folder shipped with the extension, parsing
/// each <c>&lt;name&gt;/SKILL.md</c>'s YAML frontmatter for <c>name:</c> and
/// <c>description:</c>. The full SKILL.md body is left to the installer to
/// copy verbatim — we only parse what the settings UI needs to render.
/// </summary>
public static class BundledSkillReader
{
    public static IReadOnlyList<BundledSkillInfo> Enumerate(string skillsRoot)
    {
        if (string.IsNullOrEmpty(skillsRoot) || !Directory.Exists(skillsRoot))
            return Array.Empty<BundledSkillInfo>();

        var skills = new List<BundledSkillInfo>();
        foreach (var dir in Directory.EnumerateDirectories(skillsRoot)
                                     .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
        {
            var skillMd = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillMd)) continue;
            var (frontName, frontDesc) = ReadFrontmatter(skillMd);
            var folderName = Path.GetFileName(dir);
            skills.Add(new BundledSkillInfo(
                Name: frontName ?? folderName,
                Description: frontDesc ?? ""));
        }
        return skills;
    }

    /// <summary>
    /// Tiny YAML-frontmatter parser — we only need <c>name:</c> and
    /// <c>description:</c> on their own line, both string scalars. No quoting,
    /// no folding, no nested maps. Returns (null, null) if the file has no
    /// frontmatter or the keys are absent.
    /// </summary>
    private static (string? Name, string? Description) ReadFrontmatter(string path)
    {
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return (null, null); }

        if (lines.Length == 0 || lines[0].Trim() != "---") return (null, null);

        string? name = null;
        string? desc = null;
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim() == "---") break;
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var key = line.Substring(0, idx).Trim();
            var value = line.Substring(idx + 1).Trim();
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && name is null)
                name = value;
            else if (key.Equals("description", StringComparison.OrdinalIgnoreCase) && desc is null)
                desc = value;
        }
        return (name, desc);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass.**

```powershell
dotnet test --filter FullyQualifiedName~BundledSkillReaderTests
```

Expected: 7 passed, 0 failed.

- [ ] **Step 5: Commit.**

```powershell
git add src/BundledSkillReader.cs tests/BundledSkillReaderTests.cs
git commit -m "feat(skills): BundledSkillReader enumerates and parses skill frontmatter"
```

---

## Task 3: `SkillInstaller` — copy bundled skills into and remove them from a target subdir

**Files:**
- Create: `src/SkillInstaller.cs`
- Test: `tests/SkillInstallerTests.cs`

- [ ] **Step 1: Write the failing tests.**

Create `tests/SkillInstallerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the failing tests.**

```powershell
dotnet test --filter FullyQualifiedName~SkillInstallerTests
```

Expected: build error — `SkillInstaller` does not exist.

- [ ] **Step 3: Implement `SkillInstaller`.**

Create `src/SkillInstaller.cs`:

```csharp
namespace Terminal;

/// <summary>
/// Installs bundled skill folders into (and removes them from) a CLI-specific
/// project subdirectory (e.g. <c>.claude/skills</c>, <c>.codex/skills</c>,
/// <c>.github/skills</c>). Mirrors the prev/next-diff lifecycle of the MCP
/// configurators: each Save in the modal calls <see cref="InstallAll"/> for
/// newly-selected CLIs and <see cref="RemoveAll"/> for newly-deselected ones.
/// </summary>
public sealed class SkillInstaller
{
    private readonly string projectDir;
    private readonly string bundledSkillsRoot;
    private readonly Logger log;

    public SkillInstaller(string projectDir, string bundledSkillsRoot, Logger log)
    {
        this.projectDir = projectDir;
        this.bundledSkillsRoot = bundledSkillsRoot;
        this.log = log;
    }

    /// <summary>
    /// Copy every bundled skill folder into <paramref name="targetSubdir"/>.
    /// Overwrites existing files inside matching skill folders so a Concord
    /// upgrade refreshes content. Idempotent. No-op when the bundled root is
    /// missing.
    /// </summary>
    public void InstallAll(string targetSubdir)
    {
        if (!Directory.Exists(bundledSkillsRoot))
        {
            log.Warn($"[skills] bundled root missing: {bundledSkillsRoot}");
            return;
        }
        var targetRoot = Path.Combine(projectDir, NormalizeSubdir(targetSubdir));
        Directory.CreateDirectory(targetRoot);

        foreach (var srcDir in Directory.EnumerateDirectories(bundledSkillsRoot))
        {
            var name = Path.GetFileName(srcDir);
            var dstDir = Path.Combine(targetRoot, name);
            CopyDirectory(srcDir, dstDir);
            log.Info($"[skills] installed {name} -> {dstDir}");
        }
    }

    /// <summary>
    /// Remove only the skill folders whose names appear in the bundle. User-
    /// authored sibling folders are left intact. After clean-up, if the skills
    /// directory itself is empty, remove it; same for its <c>.claude</c> /
    /// <c>.codex</c> / <c>.github</c> ancestor — but never delete an ancestor
    /// that has unrelated content (e.g. <c>.github/CODEOWNERS</c>).
    /// </summary>
    public void RemoveAll(string targetSubdir)
    {
        var normalized = NormalizeSubdir(targetSubdir);
        var targetRoot = Path.Combine(projectDir, normalized);
        if (!Directory.Exists(targetRoot)) return;

        var bundledNames = Directory.Exists(bundledSkillsRoot)
            ? new HashSet<string>(
                Directory.EnumerateDirectories(bundledSkillsRoot).Select(Path.GetFileName)!,
                StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.EnumerateDirectories(targetRoot))
        {
            var name = Path.GetFileName(dir);
            if (!bundledNames.Contains(name)) continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                log.Info($"[skills] removed {dir}");
            }
            catch (Exception ex)
            {
                log.Warn($"[skills] failed to remove {dir}: {ex.Message}");
            }
        }

        // Climb the directory tree, pruning empties — but stop before deleting
        // the project dir itself, and never delete a directory that has any
        // remaining content (so e.g. .github/CODEOWNERS keeps .github alive).
        TryPruneEmpty(targetRoot);
        var parent = Path.GetDirectoryName(targetRoot);
        if (!string.IsNullOrEmpty(parent) &&
            !Path.GetFullPath(parent).Equals(Path.GetFullPath(projectDir), StringComparison.OrdinalIgnoreCase))
        {
            TryPruneEmpty(parent);
        }
    }

    private static string NormalizeSubdir(string sub) =>
        sub.Replace('/', Path.DirectorySeparatorChar)
           .Replace('\\', Path.DirectorySeparatorChar);

    private void TryPruneEmpty(string dir)
    {
        try
        {
            if (Directory.Exists(dir) &&
                !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                log.Info($"[skills] pruned empty {dir}");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"[skills] failed to prune {dir}: {ex.Message}");
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var dst = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, dst, overwrite: true);
        }
        foreach (var sub in Directory.EnumerateDirectories(sourceDir))
        {
            var dst = Path.Combine(destDir, Path.GetFileName(sub));
            CopyDirectory(sub, dst);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass.**

```powershell
dotnet test --filter FullyQualifiedName~SkillInstallerTests
```

Expected: 9 passed, 0 failed.

- [ ] **Step 5: Commit.**

```powershell
git add src/SkillInstaller.cs tests/SkillInstallerTests.cs
git commit -m "feat(skills): SkillInstaller installs/uninstalls bundled skill folders per CLI"
```

---

## Task 4: Extend `TerminalSettings` with `SkillsEnabled` + `SkillClients`

**Files:**
- Modify: `src/TerminalSettings.cs`
- Test: `tests/TerminalSettingsTests.cs`

- [ ] **Step 1: Write failing tests.**

Append to `tests/TerminalSettingsTests.cs` (inside the existing class, before the closing brace):

```csharp
[Fact]
public void Load_NoFile_HasSkillsDisabledAndNoClients()
{
    var settings = TerminalSettings.Load(tmpDir);
    settings.SkillsEnabled.Should().BeFalse();
    settings.SkillClients.Should().BeEmpty();
}

[Fact]
public void Load_LegacyFileWithoutSkillKeys_DefaultsToDisabled()
{
    var resourcesDir = Path.Combine(tmpDir, "resources");
    Directory.CreateDirectory(resourcesDir);
    // A 1.3.x settings file: no skillsEnabled, no skillClients keys.
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
    settings.SkillsEnabled.Should().BeFalse();
    settings.SkillClients.Should().BeEmpty();
    // Sanity: existing fields still load.
    settings.McpEnabled.Should().BeTrue();
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
```

- [ ] **Step 2: Run failing tests.**

```powershell
dotnet test --filter FullyQualifiedName~TerminalSettingsTests
```

Expected: build error — `SkillsEnabled` and `SkillClients` are not members of `TerminalSettings`.

- [ ] **Step 3: Add the two fields with defaults, migration, and serialization.**

Edit `src/TerminalSettings.cs`. Update the record definition (line 5):

Replace:

```csharp
public sealed record TerminalSettings(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines,
    string Theme,
    bool McpEnabled,
    int McpPort,
    string[] McpClients,
    bool McpServerEnabled,
    int McpServerPort,
    bool StudioProActionsEnabled,
    bool MaiaIntegrationEnabled,
    string RefreshFromDiskHotkey,
    bool RestoreTabsOnReopen)
```

with:

```csharp
public sealed record TerminalSettings(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines,
    string Theme,
    bool McpEnabled,
    int McpPort,
    string[] McpClients,
    bool McpServerEnabled,
    int McpServerPort,
    bool StudioProActionsEnabled,
    bool MaiaIntegrationEnabled,
    string RefreshFromDiskHotkey,
    bool RestoreTabsOnReopen,
    bool SkillsEnabled,
    string[] SkillClients)
```

Update `Defaults()` (line 21). Add the two new fields at the end of the constructor call, before the closing paren:

```csharp
        RefreshFromDiskHotkey: "F4",
        RestoreTabsOnReopen: true,
        SkillsEnabled: false,
        SkillClients: Array.Empty<string>());
```

Update `Load()` to read and migrate. Inside the `try` block of `Load()`, replace the `return new TerminalSettings(...)` constructor invocation (lines 98-112) with:

```csharp
            return new TerminalSettings(
                ShellPath: MigrateShellPathForPlatform(dto.ShellPath ?? def.ShellPath),
                Args: dto.Args ?? def.Args,
                RingBufferKB: dto.RingBufferKB ?? def.RingBufferKB,
                XtermScrollbackLines: dto.XtermScrollbackLines ?? def.XtermScrollbackLines,
                Theme: dto.Theme ?? def.Theme,
                McpEnabled: dto.McpEnabled ?? def.McpEnabled,
                McpPort: dto.McpPort ?? def.McpPort,
                McpClients: dto.McpClients ?? def.McpClients,
                McpServerEnabled: master,
                McpServerPort: port,
                StudioProActionsEnabled: dto.StudioProActionsEnabled ?? def.StudioProActionsEnabled,
                MaiaIntegrationEnabled: dto.MaiaIntegrationEnabled ?? def.MaiaIntegrationEnabled,
                RefreshFromDiskHotkey: dto.RefreshFromDiskHotkey ?? def.RefreshFromDiskHotkey,
                RestoreTabsOnReopen: dto.RestoreTabsOnReopen ?? def.RestoreTabsOnReopen,
                SkillsEnabled: dto.SkillsEnabled ?? def.SkillsEnabled,
                SkillClients: dto.SkillClients ?? def.SkillClients);
```

Update `Save()` (line 120) to serialize the two new fields. Replace the `Dto` constructor inside `Save()` (lines 125-134):

```csharp
        var dto = new Dto(
            ShellPath, Args, RingBufferKB, XtermScrollbackLines, Theme,
            McpEnabled, McpPort, McpClients,
            McpServerEnabled, McpServerPort,
            StudioProActionsEnabled, MaiaIntegrationEnabled,
            RefreshFromDiskHotkey, RestoreTabsOnReopen,
            SkillsEnabled, SkillClients,
            // Legacy keys: write them too so an older Concord build that reads
            // this file keeps the master toggle in sync. Drop after 1.4.0.
            ActionsServerEnabled: McpServerEnabled,
            ActionsServerPort: McpServerPort);
```

Update the private `Dto` record (line 138). Replace the existing definition with:

```csharp
    private sealed record Dto(
        string? ShellPath,
        string[]? Args,
        int? RingBufferKB,
        int? XtermScrollbackLines,
        string? Theme,
        bool? McpEnabled,
        int? McpPort,
        string[]? McpClients,
        bool? McpServerEnabled,
        int? McpServerPort,
        bool? StudioProActionsEnabled,
        bool? MaiaIntegrationEnabled,
        string? RefreshFromDiskHotkey,
        bool? RestoreTabsOnReopen,
        bool? SkillsEnabled,
        string[]? SkillClients,
        bool? ActionsServerEnabled = null,
        int? ActionsServerPort = null);
```

- [ ] **Step 4: Run tests to verify they pass.**

```powershell
dotnet test --filter FullyQualifiedName~TerminalSettingsTests
```

Expected: all `TerminalSettingsTests` pass (existing + 3 new).

- [ ] **Step 5: Commit.**

```powershell
git add src/TerminalSettings.cs tests/TerminalSettingsTests.cs
git commit -m "feat(skills): add SkillsEnabled + SkillClients to TerminalSettings"
```

---

## Task 5: Extend message DTOs

**Files:**
- Modify: `src/Messages/Outgoing.cs`
- Modify: `src/Messages/Incoming.cs`
- Test: `tests/MessageDtoTests.cs` (extend if it exercises `SettingsPayload`; otherwise no test change needed — coverage comes via `TerminalPaneViewModel` integration in Task 6)

- [ ] **Step 1: Add `BundledSkillPayload` and extend `SettingsPayload`.**

Edit `src/Messages/Outgoing.cs`. Below the existing `ShellOptionPayload` record, add:

```csharp
public sealed record BundledSkillPayload(string Name, string Description);
```

Replace the `SettingsPayload` record (lines 24-42):

```csharp
public sealed record SettingsPayload(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines,
    string Theme,
    IReadOnlyList<ShellOptionPayload> AvailableShells,
    bool McpEnabled,
    int McpPort,
    string[] McpClients,
    bool McpServerEnabled,
    int McpServerPort,
    bool StudioProActionsEnabled,
    bool MaiaIntegrationEnabled,
    string Platform,
    string RefreshFromDiskHotkey,
    bool RestoreTabsOnReopen,
    AboutInfoPayload About,
    StudioProMcpInfoPayload? StudioProMcp,
    bool SkillsEnabled,
    string[] SkillClients,
    IReadOnlyList<BundledSkillPayload> BundledSkills);
```

- [ ] **Step 2: Extend `SaveSettingsPayload`.**

Edit `src/Messages/Incoming.cs`. Replace the existing `SaveSettingsPayload` (lines 18-32):

```csharp
public sealed record SaveSettingsPayload(
    string ShellPath,
    string[] Args,
    int? RingBufferKB = null,
    int? XtermScrollbackLines = null,
    string? Theme = null,
    bool? McpEnabled = null,
    int? McpPort = null,
    string[]? McpClients = null,
    bool? McpServerEnabled = null,
    int? McpServerPort = null,
    bool? StudioProActionsEnabled = null,
    bool? MaiaIntegrationEnabled = null,
    string? RefreshFromDiskHotkey = null,
    bool? RestoreTabsOnReopen = null,
    bool? SkillsEnabled = null,
    string[]? SkillClients = null);
```

- [ ] **Step 3: Build to verify type-checks.**

```powershell
dotnet build
```

Expected: build will FAIL because `TerminalPaneViewModel.BuildSettingsPayload` does not yet pass the new fields. That's expected — Task 6 fixes it.

- [ ] **Step 4: Commit (intentionally breaking — Task 6 is the matched pair).**

```powershell
git add src/Messages/Outgoing.cs src/Messages/Incoming.cs
git commit -m "feat(skills): extend Settings/SaveSettings DTOs with skills fields"
```

---

## Task 6: Wire skills into `TerminalPaneViewModel` and resolve bundled root via MEF

**Files:**
- Modify: `src/TerminalPaneExtension.cs`
- Modify: `src/TerminalPaneViewModel.cs`

**Why:** `TerminalPaneExtension` already constructs the view model and has access to MEF-injected services. We add `IExtensionFileService` to its `[ImportingConstructor]`, resolve the bundled-skills root path once, and pass it through. The view model gains `ApplySkillsConfig` (mirroring `ApplyMcpConfig` / `ApplyActionsMcpConfig`) and surfaces the bundled skills list in `BuildSettingsPayload`.

- [ ] **Step 1: Inject `IExtensionFileService` into `TerminalPaneExtension`.**

Edit `src/TerminalPaneExtension.cs`. Add the import at the top:

```csharp
using Mendix.StudioPro.ExtensionsAPI.Services;
```

Replace the constructor signature (lines 36-41):

```csharp
    private readonly IExtensionFileService extensionFileService;

    [ImportingConstructor]
    public TerminalPaneExtension(
        ILocalRunConfigurationsService localRunConfigs,
        IExtensionFileService extensionFileService)
    {
        this.localRunConfigs = localRunConfigs;
        this.extensionFileService = extensionFileService;
        manager = new TerminalSessionManager(new PtyNetFactory());
    }
```

In `Open()` (lines 63-75), update the `TerminalPaneViewModel` constructor call to pass the bundled-skills root:

Replace:

```csharp
        var vm = new TerminalPaneViewModel(
            title: "Concord",
            manager: manager,
            getCurrentApp: () => CurrentApp,
            webIndexUri: indexUri,
            log: log,
            getApplicationRootUrl: () =>
            {
                var model = CurrentApp;
                if (model is null) return null;
                try { return localRunConfigs.GetActiveConfiguration(model)?.ApplicationRootUrl; }
                catch (Exception ex) { log?.Warn($"GetActiveConfiguration threw: {ex.Message}"); return null; }
            });
```

with:

```csharp
        // Resolve once at Open(); the path is stable for the lifetime of the
        // extension (it's the deployed extensions/Concord/skills/ folder).
        var bundledSkillsRoot = extensionFileService.ResolvePath("skills");
        log?.Info($"[skills] bundled-root={bundledSkillsRoot}");

        var vm = new TerminalPaneViewModel(
            title: "Concord",
            manager: manager,
            getCurrentApp: () => CurrentApp,
            webIndexUri: indexUri,
            log: log,
            getApplicationRootUrl: () =>
            {
                var model = CurrentApp;
                if (model is null) return null;
                try { return localRunConfigs.GetActiveConfiguration(model)?.ApplicationRootUrl; }
                catch (Exception ex) { log?.Warn($"GetActiveConfiguration threw: {ex.Message}"); return null; }
            },
            bundledSkillsRoot: bundledSkillsRoot);
```

- [ ] **Step 2: Add `bundledSkillsRoot` parameter to `TerminalPaneViewModel`.**

Edit `src/TerminalPaneViewModel.cs`. Replace the constructor (lines 32-46):

```csharp
    private readonly TerminalSessionManager manager;
    private readonly Func<IModel?> getCurrentApp;
    private readonly Uri webIndexUri;
    private readonly Logger log;
    private readonly Func<string?> getApplicationRootUrl;
    private readonly string bundledSkillsRoot;

    private IWebView? webView;
    private string? lastKnownProjectDir;

    public TerminalPaneViewModel(
        string title,
        TerminalSessionManager manager,
        Func<IModel?> getCurrentApp,
        Uri webIndexUri,
        Logger log,
        Func<string?> getApplicationRootUrl,
        string bundledSkillsRoot)
    {
        Title = title;
        this.manager = manager;
        this.getCurrentApp = getCurrentApp;
        this.webIndexUri = webIndexUri;
        this.log = log;
        this.getApplicationRootUrl = getApplicationRootUrl;
        this.bundledSkillsRoot = bundledSkillsRoot;
    }
```

(Note: the original `lastKnownProjectDir` xmldoc comment is preserved by leaving the original triple-slash line above the field; this snippet only shows the field declarations and constructor body.)

- [ ] **Step 3: Add `ApplySkillsConfig` and integrate into save flow.**

Edit `src/TerminalPaneViewModel.cs`. After the `ApplyActionsMcpConfig` method (ends around line 443), insert:

```csharp
    /// <summary>
    /// Diff the skills toggle and per-CLI selection. For each newly-selected
    /// CLI: install bundled skills into its native subdir. For each newly-
    /// deselected CLI (or any CLI when SkillsEnabled flips off): remove only
    /// Concord-bundled skill folders, leaving user-authored siblings intact.
    /// Returns labels for the result banner.
    /// </summary>
    private string[] ApplySkillsConfig(string projectDir, TerminalSettings prev, TerminalSettings next)
    {
        var prevClients = prev.SkillsEnabled
            ? new HashSet<string>(prev.SkillClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextClients = next.SkillsEnabled
            ? new HashSet<string>(next.SkillClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var installer = new SkillInstaller(projectDir, bundledSkillsRoot, log);
        var touched = new List<string>();

        var perCli = new (string Key, string Label, string Subdir)[]
        {
            ("claude",  "Claude Code skills",       Path.Combine(".claude", "skills")),
            ("copilot", "Copilot CLI skills",       Path.Combine(".github", "skills")),
            ("codex",   "Codex skills",             Path.Combine(".codex",  "skills")),
        };

        log.Info($"[skills] diff prev={{{string.Join(",", prevClients)}}} next={{{string.Join(",", nextClients)}}} bundled-root={bundledSkillsRoot}");

        foreach (var (key, label, subdir) in perCli)
        {
            var was = prevClients.Contains(key);
            var now = nextClients.Contains(key);
            try
            {
                if (now && !was)       { installer.InstallAll(subdir); touched.Add(label); }
                else if (now && was)   { installer.InstallAll(subdir); /* refresh on every save */ }
                else if (!now && was)  { installer.RemoveAll(subdir);  touched.Add(label + " (removed)"); }
            }
            catch (Exception ex)
            {
                log.Error($"[skills] {label} apply failed", ex);
            }
        }

        return touched.ToArray();
    }
```

In `HandleSaveSettings`, find this block (around line 312):

```csharp
            // 3. Apply file changes BEFORE saving settings.
            var touchedPrimary = ApplyMcpConfig(dir, current, updated);
            var touchedActions = ApplyActionsMcpConfig(dir, current, updated);

            updated.Save(dir);
            Post("settings", BuildSettingsPayload(updated));

            var allTouched = touchedPrimary.Concat(touchedActions).ToArray();
```

Replace with:

```csharp
            // 3. Apply file changes BEFORE saving settings.
            var touchedPrimary = ApplyMcpConfig(dir, current, updated);
            var touchedActions = ApplyActionsMcpConfig(dir, current, updated);
            var touchedSkills  = ApplySkillsConfig(dir, current, updated);

            updated.Save(dir);
            Post("settings", BuildSettingsPayload(updated));

            var allTouched = touchedPrimary.Concat(touchedActions).Concat(touchedSkills).ToArray();
```

In the same method, find the field-update `var updated = current with { ... }` block (around line 293) and add the two new fields. Replace:

```csharp
            var updated = current with
            {
                ShellPath = p.ShellPath,
                Args = p.Args,
                RingBufferKB = p.RingBufferKB ?? current.RingBufferKB,
                XtermScrollbackLines = p.XtermScrollbackLines ?? current.XtermScrollbackLines,
                Theme = p.Theme ?? current.Theme,
                McpEnabled = newEnabled,
                McpPort = newPort,
                McpClients = newClients,
                McpServerEnabled = newMcpServerEnabled,
                McpServerPort = newMcpServerPort,
                StudioProActionsEnabled = newStudioProActions,
                MaiaIntegrationEnabled = newMaiaIntegration,
                RefreshFromDiskHotkey = newRefreshHotkey,
                RestoreTabsOnReopen = newRestoreTabs,
            };
```

with:

```csharp
            var newSkillsEnabled = p.SkillsEnabled ?? current.SkillsEnabled;
            var newSkillClients  = p.SkillClients  ?? current.SkillClients;

            var updated = current with
            {
                ShellPath = p.ShellPath,
                Args = p.Args,
                RingBufferKB = p.RingBufferKB ?? current.RingBufferKB,
                XtermScrollbackLines = p.XtermScrollbackLines ?? current.XtermScrollbackLines,
                Theme = p.Theme ?? current.Theme,
                McpEnabled = newEnabled,
                McpPort = newPort,
                McpClients = newClients,
                McpServerEnabled = newMcpServerEnabled,
                McpServerPort = newMcpServerPort,
                StudioProActionsEnabled = newStudioProActions,
                MaiaIntegrationEnabled = newMaiaIntegration,
                RefreshFromDiskHotkey = newRefreshHotkey,
                RestoreTabsOnReopen = newRestoreTabs,
                SkillsEnabled = newSkillsEnabled,
                SkillClients = newSkillClients,
            };
```

- [ ] **Step 4: Update `BuildResultMessage` to cover skill-only saves.**

Find `BuildResultMessage` (line 345) and replace:

```csharp
    private static string BuildResultMessage(TerminalSettings s, string[] touched) =>
        (s.McpEnabled || s.McpServerEnabled)
            ? $"MCP servers updated for {string.Join(", ", touched)}. Restarting open terminals…"
            : $"MCP servers disabled (cleaned up: {string.Join(", ", touched)}). Restarting open terminals…";
```

with:

```csharp
    private static string BuildResultMessage(TerminalSettings s, string[] touched)
    {
        var any = s.McpEnabled || s.McpServerEnabled || s.SkillsEnabled;
        return any
            ? $"Concord wired up: {string.Join(", ", touched)}. Restarting open terminals…"
            : $"Concord cleaned up: {string.Join(", ", touched)}. Restarting open terminals…";
    }
```

(Generalized to cover MCP, Concord MCP, and skills uniformly.)

- [ ] **Step 5: Surface bundled skills + new fields in `BuildSettingsPayload`.**

Replace the `BuildSettingsPayload` body (line 536-568):

```csharp
    private SettingsPayload BuildSettingsPayload(TerminalSettings s)
    {
        var dir = GetProjectDir();
        var settingsPath = dir != null
            ? System.IO.Path.Combine(dir, "resources", "terminal-settings.json")
            : null;

        var bundled = BundledSkillReader.Enumerate(bundledSkillsRoot)
            .Select(b => new BundledSkillPayload(b.Name, b.Description))
            .ToList();

        return new SettingsPayload(
            ShellPath: s.ShellPath,
            Args: s.Args,
            RingBufferKB: s.RingBufferKB,
            XtermScrollbackLines: s.XtermScrollbackLines,
            Theme: s.Theme,
            AvailableShells: ShellDetector.Detect()
                .Select(o => new ShellOptionPayload(o.Name, o.Path))
                .ToList(),
            McpEnabled: s.McpEnabled,
            McpPort: s.McpPort,
            McpClients: s.McpClients,
            McpServerEnabled: s.McpServerEnabled,
            McpServerPort: manager.CurrentActionServerPort ?? s.McpServerPort,
            StudioProActionsEnabled: s.StudioProActionsEnabled,
            MaiaIntegrationEnabled: s.MaiaIntegrationEnabled,
            Platform: OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : "linux",
            RefreshFromDiskHotkey: s.RefreshFromDiskHotkey,
            RestoreTabsOnReopen: s.RestoreTabsOnReopen,
            About: new AboutInfoPayload(
                Version: ResolveBuildVersion(),
                LogPath: log?.Path,
                SettingsPath: settingsPath),
            StudioProMcp: ProbeStudioProMcp(),
            SkillsEnabled: s.SkillsEnabled,
            SkillClients: s.SkillClients,
            BundledSkills: bundled);
    }
```

- [ ] **Step 6: Build and run the full test suite.**

```powershell
dotnet build
dotnet test
```

Expected: build succeeds; all tests pass.

- [ ] **Step 7: Commit.**

```powershell
git add src/TerminalPaneExtension.cs src/TerminalPaneViewModel.cs
git commit -m "feat(skills): wire SkillInstaller into save flow with bundled-root resolution"
```

---

## Task 7: UI — replace the "Coming soon" Skills section with full controls

**Files:**
- Modify: `ui/index.html`
- Modify: `ui/src/settings-modal.ts`

- [ ] **Step 1: Replace the Skills section markup in `ui/index.html`.**

Find the existing Skills section (lines 852-861):

```html
          <!-- Skills (placeholder) -->
          <section class="settings-section" data-section="skills" role="tabpanel">
            <h4>Skills</h4>
            <p class="section-desc">Install prescriptive skill packs that teach Studio Pro patterns it doesn't ship with — files written into your Mendix project so Studio Pro can use them immediately.</p>
            <div class="coming-soon">
              <span class="icon" data-icon="packagePlus"></span>
              <div class="title">Coming soon</div>
              <p>Skill packs will appear here once the registry is wired up.</p>
            </div>
          </section>
```

Replace with:

```html
          <!-- Skills -->
          <section class="settings-section" data-section="skills" role="tabpanel">
            <h4>Skills</h4>
            <p class="section-desc">
              Mendix skill packs are prescriptive playbooks Concord writes into your project so the CLIs above know how to drive Studio Pro's MCP and Concord MCP correctly. Concord ships the bundled list below; enable per-CLI to install.
            </p>
            <div class="checkbox-row">
              <input id="set-skills-enabled" type="checkbox">
              <label for="set-skills-enabled" style="margin:0">Enable Mendix skill pack</label>
            </div>

            <h5>Bundled in this Concord</h5>
            <ul id="bundled-skills-list" class="bundled-skills-list"></ul>

            <h5>CLIs to install for</h5>
            <div class="checkbox-row"><input id="set-skills-claude"  type="checkbox"><label for="set-skills-claude"  style="margin:0">Claude Code <span class="muted">— writes <code>&lt;project&gt;/.claude/skills/</code></span></label></div>
            <div class="checkbox-row"><input id="set-skills-copilot" type="checkbox"><label for="set-skills-copilot" style="margin:0">GitHub Copilot CLI <span class="muted">— writes <code>&lt;project&gt;/.github/skills/</code></span></label></div>
            <div class="checkbox-row"><input id="set-skills-codex"   type="checkbox"><label for="set-skills-codex"   style="margin:0">Codex <span class="muted">— writes <code>&lt;project&gt;/.codex/skills/</code></span></label></div>
          </section>
```

- [ ] **Step 2: Add CSS for the bundled skills list.**

Find the `.coming-soon` block in the same file (line 432) and append a sibling rule below it (or anywhere within the existing `<style>` block — choose a location near other list-style rules):

```css
    .bundled-skills-list {
      list-style: none;
      padding: 8px 12px;
      margin: 4px 0 12px;
      border: 1px solid var(--border);
      border-radius: 4px;
      background: var(--surface-2, rgba(255, 255, 255, 0.03));
      max-height: 200px;
      overflow-y: auto;
    }
    .bundled-skills-list li {
      padding: 4px 0;
      font-size: 12px;
    }
    .bundled-skills-list li .skill-name {
      font-family: var(--mono, monospace);
      font-weight: 600;
    }
    .bundled-skills-list li .skill-desc {
      display: block;
      color: var(--muted, #888);
      font-size: 11px;
      margin-top: 2px;
      line-height: 1.4;
    }
    .bundled-skills-list:empty::before {
      content: "No bundled skills found.";
      color: var(--muted, #888);
      font-size: 12px;
    }
```

(The `var(--surface-2, ...)` and `var(--mono, ...)` values use the project's existing theme tokens; the fallback expressions match the patterns used elsewhere in the same stylesheet.)

- [ ] **Step 3: Update the TypeScript types in `settings-modal.ts`.**

Replace the `SettingsPayload` interface (lines 22-41):

```ts
interface SettingsPayload {
  shellPath: string;
  args: string[];
  ringBufferKB: number;
  xtermScrollbackLines: number;
  theme: string;
  availableShells: ShellOption[];
  mcpEnabled: boolean;
  mcpPort: number;
  mcpClients: string[];
  mcpServerEnabled: boolean;
  mcpServerPort: number;
  studioProActionsEnabled: boolean;
  maiaIntegrationEnabled: boolean;
  platform: string;
  refreshFromDiskHotkey: string;
  restoreTabsOnReopen: boolean;
  about: AboutInfo;
  studioProMcp: StudioProMcpInfo | null;
  skillsEnabled: boolean;
  skillClients: string[];
  bundledSkills: BundledSkill[];
}

interface BundledSkill {
  name: string;
  description: string;
}
```

- [ ] **Step 4: Add the new DOM-element fields and event wiring.**

In the `SettingsModal` class, after the existing `inpRefreshHotkey` field declaration (line 113), add:

```ts
  private chkSkillsEnabled = document.getElementById(
    "set-skills-enabled",
  ) as HTMLInputElement;
  private chkSkillsClaude = document.getElementById(
    "set-skills-claude",
  ) as HTMLInputElement;
  private chkSkillsCopilot = document.getElementById(
    "set-skills-copilot",
  ) as HTMLInputElement;
  private chkSkillsCodex = document.getElementById(
    "set-skills-codex",
  ) as HTMLInputElement;
  private bundledSkillsList = document.getElementById(
    "bundled-skills-list",
  ) as HTMLUListElement;
```

In the constructor (around line 137 where `chkActions` event listener is wired), add the master-toggle listener:

```ts
    this.chkSkillsEnabled.addEventListener("change", () =>
      this.onSkillsEnabledChange(),
    );
```

- [ ] **Step 5: Add `onSkillsEnabledChange`.**

Below the existing `onActionsEnabledChange` method (around line 233):

```ts
  /** When the master Skills toggle flips, sync the per-CLI checkboxes:
   *  - turning OFF unchecks them and disables them
   *  - turning ON re-enables them (leaves their last values alone) */
  private onSkillsEnabledChange() {
    const enabled = this.chkSkillsEnabled.checked;
    if (!enabled) {
      this.chkSkillsClaude.checked = false;
      this.chkSkillsCopilot.checked = false;
      this.chkSkillsCodex.checked = false;
    }
    this.chkSkillsClaude.disabled = !enabled;
    this.chkSkillsCopilot.disabled = !enabled;
    this.chkSkillsCodex.disabled = !enabled;
  }
```

- [ ] **Step 6: Populate the bundled-skills list and skills toggles in `populate()`.**

In `populate(d: SettingsPayload)` (around line 247), after the existing `applyMaiaPlatformGate` / `inpRefreshHotkey` block, before `populateAbout(d.about)`, add:

```ts
    // Skills
    this.chkSkillsEnabled.checked = !!d.skillsEnabled;
    const skillClients = new Set((d.skillClients ?? []).map((c) => c.toLowerCase()));
    this.chkSkillsClaude.checked  = skillClients.has("claude");
    this.chkSkillsCopilot.checked = skillClients.has("copilot");
    this.chkSkillsCodex.checked   = skillClients.has("codex");
    this.onSkillsEnabledChange();
    this.renderBundledSkillsList(d.bundledSkills ?? []);
```

- [ ] **Step 7: Add `renderBundledSkillsList`.**

Below the existing `populateAbout` method (around line 341), add:

```ts
  private renderBundledSkillsList(skills: BundledSkill[]) {
    this.bundledSkillsList.replaceChildren();
    for (const s of skills) {
      const li = document.createElement("li");

      const nameEl = document.createElement("span");
      nameEl.className = "skill-name";
      nameEl.textContent = s.name;
      li.appendChild(nameEl);

      if (s.description) {
        const descEl = document.createElement("span");
        descEl.className = "skill-desc";
        descEl.textContent = s.description;
        li.appendChild(descEl);
      }
      this.bundledSkillsList.appendChild(li);
    }
  }
```

- [ ] **Step 8: Include skills fields in the save payload.**

In `save()` (around line 385), build the `skillClients` array and include both new fields. After the existing `mcpClients` array construction:

```ts
    const skillClients: string[] = [];
    if (this.chkSkillsClaude.checked)  skillClients.push("claude");
    if (this.chkSkillsCopilot.checked) skillClients.push("copilot");
    if (this.chkSkillsCodex.checked)   skillClients.push("codex");
```

In the same method, find the `this.bridge.send("saveSettings", { ... })` call (around line 398) and add the two new fields to the payload object before the closing brace:

```ts
      restoreTabsOnReopen: this.chkRestoreTabs.checked,
      skillsEnabled: this.chkSkillsEnabled.checked,
      skillClients,
```

- [ ] **Step 9: Build the UI bundle and verify no TS errors.**

```powershell
node ui/esbuild.mjs
```

Expected: no compilation errors. The bundle output `wwwroot/terminal.bundle.js` is updated.

- [ ] **Step 10: Build the full extension to confirm end-to-end.**

```powershell
dotnet build
```

Expected: build succeeds (the BuildUi target re-runs esbuild; DeployToMendix copies output to your Mendix target if `MendixDeployTarget` is configured).

- [ ] **Step 11: Commit.**

```powershell
git add ui/index.html ui/src/settings-modal.ts
git commit -m "feat(skills): replace Coming Soon with full Skills UI"
```

---

## Task 8: Manual end-to-end verification

**Files:** None (documentation step). Capture results in your local notes; do not commit.

**Why:** TS-side coverage is via the existing build-bundle target; the skills install/uninstall behavior was unit-tested in Task 3, but the modal-driven save flow + tab-recycle interaction needs a human run.

- [ ] **Step 1: Deploy Concord to a test Mendix project.**

In `Directory.Build.props`, set `MendixDeployTarget` to a Mendix 11.10+ project root that you can open in Studio Pro. Then:

```powershell
dotnet build
```

Expected: `<MendixDeployTarget>\extensions\Concord\skills\` contains the 7 skill folders.

- [ ] **Step 2: Open the project in Studio Pro and the Concord pane.**

Settings → Skills tab.

Expected: the section now shows the master toggle, "Bundled in this Concord" list (7 entries), and three CLI checkboxes with target paths.

- [ ] **Step 3: Enable skills for Claude Code and save.**

Tick "Enable Mendix skill pack" and "Claude Code". Save.

Expected: notice banner: `"Concord wired up: Claude Code skills. Restarting open terminals…"`. Verify on disk:

```powershell
Test-Path "<MendixDeployTarget>\.claude\skills\mendix-microflow-update\SKILL.md"
```

Expected: `True`. All 7 skill folders should be present under `.claude/skills/`.

- [ ] **Step 4: Add a hand-authored skill alongside the bundled ones.**

```powershell
$dir = "<MendixDeployTarget>\.claude\skills\my-thing"
New-Item -ItemType Directory -Path $dir -Force | Out-Null
Set-Content "$dir\SKILL.md" "---`nname: my-thing`ndescription: User authored.`n---`nbody`n"
```

- [ ] **Step 5: Disable Claude Code, save.**

Untick "Claude Code", save.

Expected: bundled skill folders gone, `my-thing` survives.

```powershell
Test-Path "<MendixDeployTarget>\.claude\skills\mendix-microflow-update"  # False
Test-Path "<MendixDeployTarget>\.claude\skills\my-thing\SKILL.md"        # True
```

- [ ] **Step 6: Re-enable for all three CLIs, save.**

Tick all three CLI checkboxes, save.

Expected: banner names all three; on disk:

```powershell
Test-Path "<MendixDeployTarget>\.claude\skills\mendix-page-gen\SKILL.md"   # True
Test-Path "<MendixDeployTarget>\.codex\skills\mendix-page-gen\SKILL.md"    # True
Test-Path "<MendixDeployTarget>\.github\skills\mendix-page-gen\SKILL.md"   # True
```

- [ ] **Step 7: Open Claude Code in a Concord terminal tab and confirm a skill loads.**

In the Concord terminal, run `claude`. In the prompt, type `/help` or invoke a skill referenced by description (e.g. ask Claude to plan a microflow change). Claude Code should auto-discover the skills from `<project>/.claude/skills/` (no manual registration needed).

Expected: Claude Code's output reflects the skill content (e.g. follows the 70px edge-to-edge rule from `mendix-microflow-update`).

- [ ] **Step 8: Smoke-test Codex and Copilot CLI auto-discovery (per spec § Risks).**

In separate Concord terminals, run `codex` and `copilot` (or `gh copilot`). Confirm each CLI either auto-discovers the project-local skills directory or surfaces a "skills found" notice. If a CLI does NOT auto-discover from the documented location, file a follow-up to swap that CLI's `targetSubdir` constant in `ApplySkillsConfig` (Task 6).

Expected: both CLIs see the bundled skills. If not, note which CLI/version diverges and update the spec's "Risks & open questions" section.

---

## Task 9: Update README and CHANGELOG

**Files:**
- Modify: `README.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Replace the Skills bullet in `README.md`.**

Find line 83 (in the "Settings panel" section):

```markdown
5. **Skills** — placeholder. Coming feature: install prescriptive skill packs that Concord writes into your Mendix project tree to teach Studio Pro patterns it doesn't ship with.
```

Replace with:

```markdown
5. **Skills** — install bundled Mendix skill packs into the open project. Master toggle + per-CLI checkboxes (Claude Code → `.claude/skills/`, Copilot CLI → `.github/skills/`, Codex → `.codex/skills/`). Each Save refreshes the bundled folders so a Concord upgrade ships new skills automatically; user-authored skills sitting alongside in the same directory are left intact.
```

- [ ] **Step 2: Add a CHANGELOG entry.**

Open `CHANGELOG.md`. At the top of the entries list, add a new section for the next version (replace `1.4.0` with the actual planned version if different):

```markdown
## 1.4.0

### Added

- **Bundled Mendix skill packs.** The Skills section of the settings modal is now a working installer: enable per-CLI to write the Concord-bundled skills into `<project>/.claude/skills/`, `<project>/.github/skills/`, and/or `<project>/.codex/skills/`. Disable a CLI to remove only the Concord-bundled folders — user-authored siblings under the same directory are left intact. Each Save refreshes the bundled content so a Concord upgrade ships new or updated skills automatically.
- **7 Mendix skills** ship in this release: `mendix-microflow-common`, `mendix-microflow-syntax`, `mendix-microflow-update`, `mendix-page-gen`, `mendix-view-entities`, `mendix-workflow-common`, `mendix-workflow-update`.

### Notes

- Skills are installed project-local only in this release (no `~/.claude/skills/` writes).
- If you have hand-edited a Concord-bundled skill folder, your edits will be overwritten on the next Save. Add custom skills as siblings (e.g. `<project>/.claude/skills/my-thing/`) to keep them safe across upgrades.
```

- [ ] **Step 3: Commit.**

```powershell
git add README.md CHANGELOG.md
git commit -m "docs: README + CHANGELOG for bundled Skills installer"
```

---

## Self-Review (already performed; recorded here for traceability)

**Spec coverage:**
- Bundling — Task 1 ✓
- `SkillInstaller` (install / remove / preserve user folders) — Task 3 ✓
- `SkillsEnabled` + `SkillClients` settings + migration — Task 4 ✓
- DTO additions — Task 5 ✓
- Save-flow integration + bundled-root resolution — Task 6 ✓
- UI replacement (master toggle, bundled list, per-CLI checkboxes) — Task 7 ✓
- Result banner update — Task 6 (Step 4) ✓
- Tab-recycle reuses existing `allTouched` flow — Task 6 (Step 3) ✓
- Manual Codex/Copilot discovery verification — Task 8 ✓

**Placeholder scan:** No "TBD", no "implement later", no "similar to Task N" — every code block is complete and self-contained.

**Type consistency:** `BundledSkillInfo` (C#) ↔ `BundledSkillPayload` (DTO) ↔ `BundledSkill` (TS). `SkillsEnabled` and `SkillClients` are the same identifier across `TerminalSettings`, both `Outgoing.cs` and `Incoming.cs` payloads, and the TS interface (camelCased). `ApplySkillsConfig` returns `string[]` matching `ApplyMcpConfig` and `ApplyActionsMcpConfig`. `SkillInstaller` constructor signature in Task 3 matches the constructor used in Task 6.
