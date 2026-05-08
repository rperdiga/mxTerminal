using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class McpJsonConfiguratorTests : IDisposable
{
    private readonly string tmpDir;
    private readonly string filePath;

    public McpJsonConfiguratorTests()
    {
        tmpDir = Path.Combine(Path.GetTempPath(), "mcpjson-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        filePath = Path.Combine(tmpDir, ".mcp.json");
    }

    public void Dispose() => Directory.Delete(tmpDir, recursive: true);

    [Fact]
    public void Upsert_NoFile_CreatesFileWithEntry()
    {
        new McpJsonConfigurator(tmpDir).Upsert("http://localhost:7782/mcp");
        File.Exists(filePath).Should().BeTrue();
        var json = File.ReadAllText(filePath);
        json.Should().Contain("\"mendix-studio-pro\"");
        json.Should().Contain("\"http://localhost:7782/mcp\"");
        json.Should().Contain("\"http\"");
    }

    [Fact]
    public void Upsert_TwiceWithDifferentUrl_OverwritesUrl()
    {
        var c = new McpJsonConfigurator(tmpDir);
        c.Upsert("http://localhost:7782/mcp");
        c.Upsert("http://localhost:9999/mcp");
        File.ReadAllText(filePath).Should().Contain("9999").And.NotContain("7782");
    }

    [Fact]
    public void Upsert_PreservesUnrelatedTopLevelKeys()
    {
        File.WriteAllText(filePath, """{"foo":"bar","mcpServers":{"other":{"type":"http","url":"http://x"}}}""");
        new McpJsonConfigurator(tmpDir).Upsert("http://localhost:7782/mcp");
        var json = File.ReadAllText(filePath);
        json.Should().Contain("foo");
        json.Should().Contain("bar");
        json.Should().Contain("other");
        json.Should().Contain("mendix-studio-pro");
    }

    [Fact]
    public void Remove_OurEntryGone_PreservesOthers()
    {
        File.WriteAllText(filePath,
            """{"mcpServers":{"mendix-studio-pro":{"type":"http","url":"http://localhost:7782/mcp"},"other":{"type":"http","url":"http://x"}}}""");
        new McpJsonConfigurator(tmpDir).Remove();
        var json = File.ReadAllText(filePath);
        json.Should().NotContain("mendix-studio-pro");
        json.Should().Contain("other");
    }

    [Fact]
    public void Remove_LastEntry_DeletesFile()
    {
        File.WriteAllText(filePath,
            """{"mcpServers":{"mendix-studio-pro":{"type":"http","url":"http://localhost:7782/mcp"}}}""");
        new McpJsonConfigurator(tmpDir).Remove();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void Remove_NoFile_NoOp()
    {
        Action act = () => new McpJsonConfigurator(tmpDir).Remove();
        act.Should().NotThrow();
    }

    [Fact]
    public void UpsertActions_NoFile_CreatesEntryUnderActionsServerName()
    {
        new McpJsonConfigurator(tmpDir).UpsertActions("http://localhost:7783/mcp");
        var json = File.ReadAllText(filePath);
        json.Should().Contain("\"concord-mcp\"");
        json.Should().Contain("\"http://localhost:7783/mcp\"");
    }

    [Fact]
    public void UpsertActions_AlongsidePrimary_BothPresent()
    {
        var c = new McpJsonConfigurator(tmpDir);
        c.Upsert("http://localhost:7782/mcp");
        c.UpsertActions("http://localhost:7783/mcp");
        var json = File.ReadAllText(filePath);
        json.Should().Contain("\"mendix-studio-pro\"");
        json.Should().Contain("\"concord-mcp\"");
    }

    [Fact]
    public void RemoveActions_KeepsPrimaryEntry()
    {
        var c = new McpJsonConfigurator(tmpDir);
        c.Upsert("http://localhost:7782/mcp");
        c.UpsertActions("http://localhost:7783/mcp");
        c.RemoveActions();
        var json = File.ReadAllText(filePath);
        json.Should().Contain("mendix-studio-pro");
        json.Should().NotContain("concord-mcp");
    }

    [Fact]
    public void Remove_KeepsActionsEntry()
    {
        var c = new McpJsonConfigurator(tmpDir);
        c.Upsert("http://localhost:7782/mcp");
        c.UpsertActions("http://localhost:7783/mcp");
        c.Remove();
        var json = File.ReadAllText(filePath);
        json.Should().NotContain("\"mendix-studio-pro\":");      // colon prevents matching mendix-studio-pro inside another value
        json.Should().Contain("concord-mcp");
    }

    [Fact]
    public void RemoveActions_NoFile_NoOp()
    {
        Action act = () => new McpJsonConfigurator(tmpDir).RemoveActions();
        act.Should().NotThrow();
    }

    [Fact]
    public void UpsertActions_MigratesLegacyEntryToConcordMcp()
    {
        // Pre-populate with the old (pre-v1.3.0) entry.
        File.WriteAllText(filePath,
            """{"mcpServers":{"mendix-studio-pro-actions":{"type":"http","url":"http://localhost:7783/mcp"}}}""");

        new McpJsonConfigurator(tmpDir).UpsertActions("http://localhost:7783/mcp");

        var json = File.ReadAllText(filePath);
        json.Should().Contain("\"concord-mcp\"");
        json.Should().NotContain("mendix-studio-pro-actions");

        // Sanity: exactly one entry under our actions key.
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var servers = doc.RootElement.GetProperty("mcpServers");
        servers.TryGetProperty("concord-mcp", out _).Should().BeTrue();
        servers.TryGetProperty("mendix-studio-pro-actions", out _).Should().BeFalse();
    }

    [Fact]
    public void RemoveActions_RemovesBothLegacyAndCurrentEntries()
    {
        // Pre-populate with both the legacy and current entries.
        File.WriteAllText(filePath,
            """{"mcpServers":{"mendix-studio-pro-actions":{"type":"http","url":"http://localhost:7783/mcp"},"concord-mcp":{"type":"http","url":"http://localhost:7783/mcp"}}}""");

        new McpJsonConfigurator(tmpDir).RemoveActions();

        // File may have been deleted (if it became empty); otherwise it must contain neither.
        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath);
            json.Should().NotContain("mendix-studio-pro-actions");
            json.Should().NotContain("concord-mcp");
        }
    }
}
