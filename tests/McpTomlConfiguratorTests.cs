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
        content.Should().Contain("command = \"npx\"");
        content.Should().Contain("\"http://localhost:7782/mcp\"");
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
}
