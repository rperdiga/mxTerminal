using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class McpTomlConfiguratorTests : IDisposable
{
    private readonly string tmpDir;
    private readonly string filePath;

    public McpTomlConfiguratorTests()
    {
        tmpDir = Path.Combine(Path.GetTempPath(), "mcptoml-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        filePath = Path.Combine(tmpDir, "config.toml");
    }

    public void Dispose() => Directory.Delete(tmpDir, recursive: true);

    private McpTomlConfigurator NewConfig() => new(filePath);

    [Fact]
    public void Upsert_NoFile_CreatesSection()
    {
        NewConfig().Upsert("http://localhost:7782/mcp");
        File.Exists(filePath).Should().BeTrue();
        var content = File.ReadAllText(filePath);
        content.Should().Contain("[mcp_servers.mendix-studio-pro]");
        content.Should().Contain("url = \"http://localhost:7782/mcp\"");
        content.Should().NotContain("npx");
        content.Should().NotContain("mcp-remote");
    }

    [Fact]
    public void Upsert_TwiceWithDifferentUrl_OverwritesSection()
    {
        var c = NewConfig();
        c.Upsert("http://localhost:7782/mcp");
        c.Upsert("http://localhost:9999/mcp");
        var content = File.ReadAllText(filePath);
        content.Should().Contain("9999").And.NotContain("7782");
    }

    [Fact]
    public void Upsert_PreservesUnrelatedSections()
    {
        File.WriteAllText(filePath, "[other]\nfoo = \"bar\"\n");
        NewConfig().Upsert("http://localhost:7782/mcp");
        var content = File.ReadAllText(filePath);
        content.Should().Contain("[other]");
        content.Should().Contain("[mcp_servers.mendix-studio-pro]");
    }

    [Fact]
    public void Remove_OurSectionGone_PreservesOthers()
    {
        File.WriteAllText(filePath,
            "[other]\nfoo = \"bar\"\n\n[mcp_servers.mendix-studio-pro]\ncommand = \"npx\"\n");
        NewConfig().Remove();
        var content = File.ReadAllText(filePath);
        content.Should().Contain("[other]");
        content.Should().NotContain("[mcp_servers.mendix-studio-pro]");
    }

    [Fact]
    public void Remove_NoFile_NoOp()
    {
        Action act = () => NewConfig().Remove();
        act.Should().NotThrow();
    }

    [Fact]
    public void UpsertActions_NoFile_CreatesActionsSection()
    {
        NewConfig().UpsertActions("http://localhost:7783/mcp");
        var content = File.ReadAllText(filePath);
        content.Should().Contain("[mcp_servers.concord-mcp]");
        content.Should().Contain("\"http://localhost:7783/mcp\"");
    }

    [Fact]
    public void UpsertActions_AlongsidePrimary_BothSectionsPresent()
    {
        var c = NewConfig();
        c.Upsert("http://localhost:7782/mcp");
        c.UpsertActions("http://localhost:7783/mcp");
        var content = File.ReadAllText(filePath);
        content.Should().Contain("[mcp_servers.mendix-studio-pro]");
        content.Should().Contain("[mcp_servers.concord-mcp]");
    }

    [Fact]
    public void RemoveActions_KeepsPrimarySection()
    {
        var c = NewConfig();
        c.Upsert("http://localhost:7782/mcp");
        c.UpsertActions("http://localhost:7783/mcp");
        c.RemoveActions();
        var content = File.ReadAllText(filePath);
        content.Should().Contain("[mcp_servers.mendix-studio-pro]");
        content.Should().NotContain("[mcp_servers.concord-mcp]");
    }

    [Fact]
    public void Remove_KeepsActionsSection()
    {
        var c = NewConfig();
        c.Upsert("http://localhost:7782/mcp");
        c.UpsertActions("http://localhost:7783/mcp");
        c.Remove();
        var content = File.ReadAllText(filePath);
        content.Should().NotContain("[mcp_servers.mendix-studio-pro]\n");
        content.Should().Contain("[mcp_servers.concord-mcp]");
    }

    [Fact]
    public void RemoveActions_NoFile_NoOp()
    {
        Action act = () => NewConfig().RemoveActions();
        act.Should().NotThrow();
    }

    [Fact]
    public void UpsertActions_MigratesLegacySectionToConcordMcp()
    {
        // Pre-populate with the old (pre-v1.3.0) section.
        File.WriteAllText(filePath,
            "[mcp_servers.mendix-studio-pro-actions]\nurl = \"http://localhost:7783/mcp\"\n");

        NewConfig().UpsertActions("http://localhost:7783/mcp");

        var content = File.ReadAllText(filePath);
        content.Should().Contain("[mcp_servers.concord-mcp]");
        content.Should().NotContain("[mcp_servers.mendix-studio-pro-actions]");

        // Sanity: exactly one occurrence of the new header.
        var occurrences = System.Text.RegularExpressions.Regex.Matches(content, @"\[mcp_servers\.concord-mcp\]").Count;
        occurrences.Should().Be(1);
    }

    [Fact]
    public void RemoveActions_RemovesBothLegacyAndCurrentSections()
    {
        // Pre-populate with both legacy and current sections.
        File.WriteAllText(filePath,
            "[mcp_servers.mendix-studio-pro-actions]\nurl = \"http://localhost:7783/mcp\"\n\n[mcp_servers.concord-mcp]\nurl = \"http://localhost:7783/mcp\"\n");

        NewConfig().RemoveActions();

        var content = File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty;
        content.Should().NotContain("[mcp_servers.mendix-studio-pro-actions]");
        content.Should().NotContain("[mcp_servers.concord-mcp]");
    }

    // ---- v4.2.2 regression tests: child-section strip (Item 1) ---------

    [Fact]
    public void RemoveActions_StripsOrphanChildSubsections_NoParent()
    {
        // Exact repro of Neo's machine 2026-05-10: pre-v1.3.0 Concord wrote
        // per-tool sub-sections under [mcp_servers.mendix-studio-pro-actions].
        // Earlier RemoveNamed stripped the parent on the v1.3.0 migration but
        // left these orphans, which Codex 0.128+ rejects with "invalid transport".
        File.WriteAllText(filePath,
            "model = \"gpt-5.5\"\n\n" +
            "[mcp_servers.mendix-studio-pro-actions.tools.get_app_status]\n" +
            "approval_mode = \"approve\"\n\n" +
            "[mcp_servers.mendix-studio-pro-actions.tools.save_all]\n" +
            "approval_mode = \"approve\"\n\n" +
            "[mcp_servers.mendix-studio-pro-actions.tools.run_app]\n" +
            "approval_mode = \"approve\"\n");

        NewConfig().RemoveActions();

        var content = File.ReadAllText(filePath);
        content.Should().NotContain("mendix-studio-pro-actions");
        content.Should().Contain("model = \"gpt-5.5\""); // unrelated keys preserved
    }

    [Fact]
    public void RemoveActions_StripsParentAndChildren_TogetherWithLegacy()
    {
        // Both legacy + current sections, each with child sub-sections.
        File.WriteAllText(filePath,
            "[mcp_servers.mendix-studio-pro-actions]\nurl = \"http://localhost:7783/mcp\"\n\n" +
            "[mcp_servers.mendix-studio-pro-actions.tools.run_app]\napproval_mode = \"approve\"\n\n" +
            "[mcp_servers.concord-mcp]\nurl = \"http://localhost:7783/mcp\"\n\n" +
            "[mcp_servers.concord-mcp.tools.maia__ping]\napproval_mode = \"approve\"\n\n" +
            "[mcp_servers.concord-mcp.tools.maia__health]\napproval_mode = \"approve\"\n");

        NewConfig().RemoveActions();

        var content = File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty;
        content.Should().NotContain("mendix-studio-pro-actions");
        content.Should().NotContain("concord-mcp");
    }

    [Fact]
    public void Remove_StripsParentAndChildren_PrimaryServer()
    {
        // Same hazard for the primary (mendix-studio-pro) server header.
        File.WriteAllText(filePath,
            "[mcp_servers.mendix-studio-pro]\nurl = \"http://localhost:8100/mcp\"\n\n" +
            "[mcp_servers.mendix-studio-pro.tools.ped_create_module]\napproval_mode = \"approve\"\n\n" +
            "[mcp_servers.mendix-studio-pro.tools.ped_read_document]\napproval_mode = \"approve\"\n\n" +
            "[other]\nkeep = true\n");

        NewConfig().Remove();

        var content = File.ReadAllText(filePath);
        content.Should().NotContain("mendix-studio-pro");
        content.Should().Contain("[other]"); // unrelated section preserved
    }

    [Fact]
    public void Remove_PrimaryDoesNotStripActionsChildren_NameDisambiguation()
    {
        // Critical: removing "mendix-studio-pro" must NOT strip
        // "mendix-studio-pro-actions.tools.*" children. The dot in
        // childPrefix disambiguates: `[mcp_servers.mendix-studio-pro.`
        // does not prefix `[mcp_servers.mendix-studio-pro-actions.`.
        File.WriteAllText(filePath,
            "[mcp_servers.mendix-studio-pro]\nurl = \"http://localhost:8100/mcp\"\n\n" +
            "[mcp_servers.mendix-studio-pro-actions]\nurl = \"http://localhost:7783/mcp\"\n\n" +
            "[mcp_servers.mendix-studio-pro-actions.tools.run_app]\napproval_mode = \"approve\"\n");

        NewConfig().Remove();

        var content = File.ReadAllText(filePath);
        content.Should().NotContain("[mcp_servers.mendix-studio-pro]");
        // Adjacent-named server and its children must survive.
        content.Should().Contain("[mcp_servers.mendix-studio-pro-actions]");
        content.Should().Contain("[mcp_servers.mendix-studio-pro-actions.tools.run_app]");
    }

    // ---- v4.2.2 regression tests: migration-prompt suppression (Item 2b) ---

    [Fact]
    public void SuppressMigrationPromptForProject_NoFile_CreatesTableAndEntry()
    {
        NewConfig().SuppressMigrationPromptForProject(@"C:\Workspace\MendixApps\TestApp1");

        var content = File.ReadAllText(filePath);
        content.Should().Contain("[notice.external_config_migration_prompts.project_last_prompted_at]");
        content.Should().Contain(@"'C:\Workspace\MendixApps\TestApp1' = 4070908800");
    }

    [Fact]
    public void SuppressMigrationPromptForProject_ExistingTable_AppendsEntry()
    {
        File.WriteAllText(filePath,
            "[notice.external_config_migration_prompts.project_last_prompted_at]\n" +
            "'C:\\Workspace\\MendixApps\\Other' = 1778081247\n");

        NewConfig().SuppressMigrationPromptForProject(@"C:\Workspace\MendixApps\NewApp");

        var content = File.ReadAllText(filePath);
        // Both entries present
        content.Should().Contain(@"'C:\Workspace\MendixApps\Other' = 1778081247");
        content.Should().Contain(@"'C:\Workspace\MendixApps\NewApp' = 4070908800");
    }

    [Fact]
    public void SuppressMigrationPromptForProject_ExistingEntry_UpdatesToFutureValue()
    {
        File.WriteAllText(filePath,
            "[notice.external_config_migration_prompts.project_last_prompted_at]\n" +
            "'C:\\Workspace\\MendixApps\\TestApp1' = 1778081247\n");

        NewConfig().SuppressMigrationPromptForProject(@"C:\Workspace\MendixApps\TestApp1");

        var content = File.ReadAllText(filePath);
        content.Should().Contain(@"'C:\Workspace\MendixApps\TestApp1' = 4070908800");
        content.Should().NotContain("1778081247");
    }

    [Fact]
    public void SuppressMigrationPromptForProject_AlreadyFuture_NoOp()
    {
        // If the stamp is already greater-than-or-equal-to our suppression
        // value, don't rewrite the file — avoids gratuitous I/O on every
        // Concord apply (apply runs at every Save plus first-run + upgrade-apply).
        var initial =
            "[notice.external_config_migration_prompts.project_last_prompted_at]\n" +
            "'C:\\Workspace\\MendixApps\\TestApp1' = 4070908800\n";
        File.WriteAllText(filePath, initial);
        var beforeMtime = File.GetLastWriteTimeUtc(filePath);

        // Wait a beat to make mtime difference observable if a write occurred.
        System.Threading.Thread.Sleep(20);

        NewConfig().SuppressMigrationPromptForProject(@"C:\Workspace\MendixApps\TestApp1");

        var afterMtime = File.GetLastWriteTimeUtc(filePath);
        afterMtime.Should().Be(beforeMtime, "no-op should not rewrite the file");
    }

    [Fact]
    public void SuppressMigrationPromptForProject_NullOrEmpty_NoOp()
    {
        var cfg = NewConfig();
        Action act = () => cfg.SuppressMigrationPromptForProject(string.Empty);
        act.Should().NotThrow();
        File.Exists(filePath).Should().BeFalse();
    }
}
