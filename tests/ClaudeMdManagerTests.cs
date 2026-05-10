using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class ClaudeMdManagerTests : IDisposable
{
    private readonly string tmpRoot;
    private readonly string projectDir;
    private readonly string rulesDir;
    private readonly string projectRulesDir;
    private readonly Logger log;

    public ClaudeMdManagerTests()
    {
        tmpRoot = Path.Combine(Path.GetTempPath(), "claude-md-tests-" + Guid.NewGuid().ToString("N"));
        projectDir = Path.Combine(tmpRoot, "project");
        rulesDir = Path.Combine(projectDir, ".claude", "rules");
        projectRulesDir = Path.Combine(rulesDir, RulesInstaller.ProjectFolderName);
        Directory.CreateDirectory(projectDir);
        log = new Logger(projectDir);
    }

    public void Dispose() => Directory.Delete(tmpRoot, recursive: true);

    private string ClaudeMdPath() => Path.Combine(projectDir, ClaudeMdManager.ClaudeMdFileName);

    private void SeedCanonicalRule()
    {
        Directory.CreateDirectory(rulesDir);
        File.WriteAllText(Path.Combine(rulesDir, RulesInstaller.CanonicalFileName), "# rules\n");
    }

    private void SeedProjectRule(string filename, string body = "x")
    {
        Directory.CreateDirectory(projectRulesDir);
        File.WriteAllText(Path.Combine(projectRulesDir, filename), body);
    }

    private ClaudeMdManager NewManager() => new(projectDir, ".claude/rules", log);

    [Fact]
    public void Apply_NoRulesFolder_DoesNotCreateClaudeMd()
    {
        NewManager().Apply();
        File.Exists(ClaudeMdPath()).Should().BeFalse();
    }

    [Fact]
    public void Apply_OnlyCanonicalRule_CreatesClaudeMdWithSingleImport()
    {
        SeedCanonicalRule();
        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().Contain(ClaudeMdManager.BeginMarker);
        body.Should().Contain(ClaudeMdManager.EndMarker);
        body.Should().Contain($"@.claude/rules/{RulesInstaller.CanonicalFileName}");
    }

    // v4.2.0: rules file was split into canonical + 2 sibling concord-*.md
    // files to stay under Claude Code's 40k-char per-file performance threshold.
    // The CLAUDE.md managed block must @-import all of them, canonical first
    // followed by siblings in sorted order, then project/ files.
    [Fact]
    public void Apply_CanonicalPlusSiblingConcordFiles_ImportsAllInOrder()
    {
        SeedCanonicalRule();
        Directory.CreateDirectory(rulesDir);
        File.WriteAllText(Path.Combine(rulesDir, "concord-pages-and-themes.md"), "# pages\n");
        File.WriteAllText(Path.Combine(rulesDir, "concord-model-discipline.md"), "# model\n");
        // User-authored sibling that does NOT start with concord- — must NOT
        // be auto-imported (only the project/ folder is for user content).
        File.WriteAllText(Path.Combine(rulesDir, "my-custom.md"), "# custom\n");

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        var canonicalIdx = body.IndexOf($"@.claude/rules/{RulesInstaller.CanonicalFileName}");
        var modelIdx = body.IndexOf("@.claude/rules/concord-model-discipline.md");
        var pagesIdx = body.IndexOf("@.claude/rules/concord-pages-and-themes.md");
        canonicalIdx.Should().BeGreaterOrEqualTo(0, "canonical present");
        modelIdx.Should().BeGreaterThan(canonicalIdx, "siblings follow canonical");
        pagesIdx.Should().BeGreaterThan(modelIdx, "siblings sorted by name");
        body.Should().NotContain("@.claude/rules/my-custom.md",
            "non-concord-prefixed top-level files are NOT auto-imported (user content lives in project/)");
    }

    [Fact]
    public void Apply_CanonicalPlusProjectFiles_ImportsBothInOrder()
    {
        SeedCanonicalRule();
        SeedProjectRule("zebra.md");
        SeedProjectRule("alpha.md");

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        var canonicalIdx = body.IndexOf($"@.claude/rules/{RulesInstaller.CanonicalFileName}");
        var alphaIdx = body.IndexOf("@.claude/rules/project/alpha.md");
        var zebraIdx = body.IndexOf("@.claude/rules/project/zebra.md");

        canonicalIdx.Should().BeGreaterThan(0, "canonical rule must be imported");
        alphaIdx.Should().BeGreaterThan(canonicalIdx, "project rules follow canonical");
        zebraIdx.Should().BeGreaterThan(alphaIdx, "project rules sorted alphabetically");
    }

    [Fact]
    public void Apply_ProjectFilesInSubdirs_ImportsRecursively()
    {
        SeedCanonicalRule();
        Directory.CreateDirectory(Path.Combine(projectRulesDir, "domain"));
        File.WriteAllText(Path.Combine(projectRulesDir, "domain", "naming.md"), "x");

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().Contain("@.claude/rules/project/domain/naming.md");
    }

    [Fact]
    public void Apply_ExistingClaudeMdWithoutMarkers_PrependsBlockPreservesContent()
    {
        SeedCanonicalRule();
        File.WriteAllText(ClaudeMdPath(), "# My Project\n\nUser-authored notes.\n");

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().StartWith(ClaudeMdManager.BeginMarker);
        body.Should().Contain("# My Project");
        body.Should().Contain("User-authored notes.");
        body.IndexOf(ClaudeMdManager.EndMarker).Should().BeLessThan(body.IndexOf("# My Project"),
            "managed block precedes user content");
    }

    [Fact]
    public void Apply_ExistingClaudeMdWithMarkers_RegeneratesInsidePreservesOutside()
    {
        SeedCanonicalRule();
        var preexisting =
            "# My Project\n\n" +
            ClaudeMdManager.BeginMarker + "\n" +
            "old import\n" +
            ClaudeMdManager.EndMarker + "\n\n" +
            "User trailer.\n";
        File.WriteAllText(ClaudeMdPath(), preexisting);

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().NotContain("old import");
        body.Should().Contain($"@.claude/rules/{RulesInstaller.CanonicalFileName}");
        body.Should().Contain("# My Project");
        body.Should().Contain("User trailer.");
    }

    [Fact]
    public void Apply_MultipleManagedBlocksFromCorruptState_CollapsesToOne()
    {
        SeedCanonicalRule();
        var corrupted =
            ClaudeMdManager.BeginMarker + "\nfirst block\n" + ClaudeMdManager.EndMarker + "\n\n" +
            "Middle user content.\n\n" +
            ClaudeMdManager.BeginMarker + "\nsecond block\n" + ClaudeMdManager.EndMarker + "\n";
        File.WriteAllText(ClaudeMdPath(), corrupted);

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        var first = body.IndexOf(ClaudeMdManager.BeginMarker, StringComparison.Ordinal);
        var second = body.IndexOf(ClaudeMdManager.BeginMarker, first + 1, StringComparison.Ordinal);
        first.Should().BeGreaterOrEqualTo(0);
        second.Should().Be(-1, "duplicate managed blocks must be collapsed to one");
        body.Should().NotContain("first block");
        body.Should().NotContain("second block");
        body.Should().Contain("Middle user content.");
    }

    [Fact]
    public void Apply_NothingChangedBetweenCalls_IsIdempotent()
    {
        SeedCanonicalRule();
        NewManager().Apply();
        var first = File.ReadAllText(ClaudeMdPath());

        NewManager().Apply();
        var second = File.ReadAllText(ClaudeMdPath());

        second.Should().Be(first);
    }

    [Fact]
    public void Apply_NoCanonicalAndNoProjectFiles_StripsExistingBlock()
    {
        // Existing CLAUDE.md with a managed block, but the rules folder
        // doesn't have anything to import → block should be stripped.
        Directory.CreateDirectory(rulesDir);
        File.WriteAllText(ClaudeMdPath(),
            "# Hello\n\n" +
            ClaudeMdManager.BeginMarker + "\nstale\n" + ClaudeMdManager.EndMarker + "\n\n" +
            "Tail.\n");

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().NotContain(ClaudeMdManager.BeginMarker);
        body.Should().Contain("# Hello");
        body.Should().Contain("Tail.");
    }

    [Fact]
    public void Apply_NoCanonicalAndNoProjectFiles_NoExistingFile_DoesNothing()
    {
        Directory.CreateDirectory(rulesDir);
        NewManager().Apply();
        File.Exists(ClaudeMdPath()).Should().BeFalse();
    }

    [Fact]
    public void Remove_ExistingBlock_StripsButKeepsFile()
    {
        SeedCanonicalRule();
        NewManager().Apply();
        File.Exists(ClaudeMdPath()).Should().BeTrue();

        NewManager().Remove();

        File.Exists(ClaudeMdPath()).Should().BeTrue("file is kept even if empty after strip");
        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().NotContain(ClaudeMdManager.BeginMarker);
    }

    [Fact]
    public void Remove_PreservesContentOutsideBlock()
    {
        SeedCanonicalRule();
        var pre =
            "# My Project\n\n" +
            ClaudeMdManager.BeginMarker + "\nimport-block\n" + ClaudeMdManager.EndMarker + "\n\n" +
            "Trailer.\n";
        File.WriteAllText(ClaudeMdPath(), pre);

        NewManager().Remove();

        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().NotContain(ClaudeMdManager.BeginMarker);
        body.Should().NotContain("import-block");
        body.Should().Contain("# My Project");
        body.Should().Contain("Trailer.");
    }

    [Fact]
    public void Remove_NoClaudeMd_NoOp()
    {
        NewManager().Remove();
        File.Exists(ClaudeMdPath()).Should().BeFalse();
    }

    [Fact]
    public void Remove_ClaudeMdWithoutBlock_LeavesFileUntouched()
    {
        File.WriteAllText(ClaudeMdPath(), "# Pre-existing\n");
        var beforeMTime = File.GetLastWriteTimeUtc(ClaudeMdPath());

        NewManager().Remove();

        File.ReadAllText(ClaudeMdPath()).Should().Be("# Pre-existing\n");
        // mtime should not advance because no write occurred.
        File.GetLastWriteTimeUtc(ClaudeMdPath()).Should().Be(beforeMTime);
    }

    // --- Round-2 fixes ---

    [Fact]
    public void Apply_BlockInMiddleOfFile_ReplacesInPlace()
    {
        // Block sits between user-authored intro and trailer. After Save, the
        // block content must regenerate IN PLACE — intro stays at top,
        // trailer stays at bottom. (Round-1 review BLOCKER fix.)
        SeedCanonicalRule();
        var pre =
            "# My Project\n\n" +
            "Some intro text.\n\n" +
            ClaudeMdManager.BeginMarker + "\nstale import\n" + ClaudeMdManager.EndMarker + "\n\n" +
            "## Section After\n\n" +
            "Trailing user content.\n";
        File.WriteAllText(ClaudeMdPath(), pre);

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        // Intro must come before the (refreshed) block.
        body.IndexOf("# My Project").Should().BeLessThan(body.IndexOf(ClaudeMdManager.BeginMarker));
        body.IndexOf("Some intro text.").Should().BeLessThan(body.IndexOf(ClaudeMdManager.BeginMarker));
        // Block must come before the trailing content.
        body.IndexOf(ClaudeMdManager.EndMarker).Should().BeLessThan(body.IndexOf("## Section After"));
        // Stale import is gone, fresh import is present.
        body.Should().NotContain("stale import");
        body.Should().Contain($"@.claude/rules/{RulesInstaller.CanonicalFileName}");
    }

    [Fact]
    public void Apply_BlockAtTopOfFile_StaysAtTop()
    {
        SeedCanonicalRule();
        var pre =
            ClaudeMdManager.BeginMarker + "\nstale\n" + ClaudeMdManager.EndMarker + "\n\n" +
            "# Title After Block\n";
        File.WriteAllText(ClaudeMdPath(), pre);

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().StartWith(ClaudeMdManager.BeginMarker);
        body.Should().Contain("# Title After Block");
    }

    [Fact]
    public void Apply_BlockAtBottomOfFile_StaysAtBottom()
    {
        SeedCanonicalRule();
        var pre =
            "# Title Before\n\n" +
            "Intro paragraph.\n\n" +
            ClaudeMdManager.BeginMarker + "\nstale\n" + ClaudeMdManager.EndMarker + "\n";
        File.WriteAllText(ClaudeMdPath(), pre);

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().StartWith("# Title Before");
        body.Should().Contain("Intro paragraph.");
        // Block must be at end (after the user content).
        body.IndexOf("Intro paragraph.").Should().BeLessThan(body.IndexOf(ClaudeMdManager.BeginMarker));
        body.Should().NotContain("stale");
    }

    [Fact]
    public void Apply_OrphanBeginMarkerNoEnd_PreservedAndFreshBlockPrepended()
    {
        // User pasted a literal BEGIN marker with no matching END. Apply must
        // NOT truncate from BEGIN to EOF (round-1 BLOCKER 2). Instead, the
        // orphan content stays as user content; a fresh block is prepended.
        SeedCanonicalRule();
        var pre =
            "# Hello\n\n" +
            ClaudeMdManager.BeginMarker + "\n" +
            "user-content-after-orphan-marker\n" +
            "no end marker follows\n";
        File.WriteAllText(ClaudeMdPath(), pre);

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().Contain("# Hello", "user content above orphan must survive");
        body.Should().Contain("user-content-after-orphan-marker", "orphan content must survive");
        body.Should().Contain("no end marker follows");
        body.Should().Contain($"@.claude/rules/{RulesInstaller.CanonicalFileName}", "fresh block was added");
    }

    [Fact]
    public void Remove_OrphanBeginMarkerNoEnd_PreservesOrphanContent()
    {
        // Same orphan scenario, but on Remove(). Must not truncate.
        var pre =
            "# Hello\n\n" +
            ClaudeMdManager.BeginMarker + "\n" +
            "orphan-body\n";
        File.WriteAllText(ClaudeMdPath(), pre);

        NewManager().Remove();

        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().Contain("# Hello");
        body.Should().Contain("orphan-body", "Remove must never silently truncate orphan content");
    }

    [Fact]
    public void Apply_OrphanBeginThenWellFormedBlock_OrphanAndInterveneContentPreserved()
    {
        // Round-2 review found this edge case: an orphan BEGIN at the top of
        // the file followed later by a well-formed block. The greedy
        // first-END-after-first-BEGIN match would have treated the entire
        // span as one block, silently destroying the orphan + intervening
        // user content. Fix: FindMatchingEnd detects intervening BEGIN as
        // orphan signal; preserve orphan content, strip only the real block.
        SeedCanonicalRule();
        var pre =
            "# Hello\n\n" +
            ClaudeMdManager.BeginMarker + "\n" +
            "orphan body line one\n" +
            "orphan body line two\n\n" +
            "## Intervening user section\n\n" +
            "Some user text between orphan and real block.\n\n" +
            ClaudeMdManager.BeginMarker + "\nstale real block\n" + ClaudeMdManager.EndMarker + "\n\n" +
            "Trailing user content.\n";
        File.WriteAllText(ClaudeMdPath(), pre);

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().Contain("# Hello", "user content above orphan must survive");
        body.Should().Contain("orphan body line one", "orphan content must survive");
        body.Should().Contain("orphan body line two");
        body.Should().Contain("## Intervening user section", "intervening user content must survive");
        body.Should().Contain("Some user text between orphan and real block.");
        body.Should().Contain("Trailing user content.");
        body.Should().NotContain("stale real block", "the well-formed block must be replaced");
        body.Should().Contain($"@.claude/rules/{RulesInstaller.CanonicalFileName}", "fresh block was added");

        // Only ONE managed block in the result (the fresh one); orphan BEGIN
        // is preserved but does NOT count as a managed block since it has no
        // END. Count of well-formed blocks = 1.
        var matches = System.Text.RegularExpressions.Regex.Matches(
            body,
            System.Text.RegularExpressions.Regex.Escape(ClaudeMdManager.BeginMarker) +
            "[\\s\\S]*?" +
            System.Text.RegularExpressions.Regex.Escape(ClaudeMdManager.EndMarker));
        matches.Count.Should().Be(1, "result should have exactly one well-formed managed block");
    }

    [Fact]
    public void Apply_RepeatedSavesOnFileWithBlockInMiddle_IsByteStable()
    {
        // After the first Apply settles content into a known shape, subsequent
        // Apply calls (with no disk-state change) must produce byte-identical
        // output. Catches whitespace drift / accumulated newlines that would
        // make every Save show as a diff.
        SeedCanonicalRule();
        File.WriteAllText(ClaudeMdPath(),
            "# Title\n\nIntro paragraph.\n\n" +
            ClaudeMdManager.BeginMarker + "\nstale\n" + ClaudeMdManager.EndMarker + "\n\n" +
            "Trailer.\n");

        NewManager().Apply();
        var first = File.ReadAllText(ClaudeMdPath());

        NewManager().Apply();
        var second = File.ReadAllText(ClaudeMdPath());

        NewManager().Apply();
        var third = File.ReadAllText(ClaudeMdPath());

        second.Should().Be(first, "second Save must produce identical bytes");
        third.Should().Be(first, "Nth Save must produce identical bytes");
    }

    [Fact]
    public void Apply_UserAuthoredImportsOutsideFence_Preserved()
    {
        // The user's CLAUDE.md may have its own @-imports outside the managed
        // block (e.g. importing their own personal rules from elsewhere). Those
        // must survive Apply unchanged. (Round-1 should-fix #3.)
        SeedCanonicalRule();
        var pre =
            "# My Project\n\n" +
            "@~/personal-rules.md\n" +
            "@./team-conventions.md\n\n" +
            "Some user notes.\n";
        File.WriteAllText(ClaudeMdPath(), pre);

        NewManager().Apply();

        var body = File.ReadAllText(ClaudeMdPath());
        body.Should().Contain("@~/personal-rules.md", "user import must survive Apply");
        body.Should().Contain("@./team-conventions.md", "user import must survive Apply");
        body.Should().Contain("Some user notes.");
        body.Should().Contain($"@.claude/rules/{RulesInstaller.CanonicalFileName}", "managed import was added");
    }
}
