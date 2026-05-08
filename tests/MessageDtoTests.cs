using System.Text.Json;
using FluentAssertions;
using Terminal.Messages;
using Xunit;

namespace Terminal.Tests;

public class MessageDtoTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CreateTab_RoundTrip_PreservesAllFields()
    {
        var json = """{"cols":120,"rows":30,"shellPath":"powershell.exe","args":["-NoLogo"],"cwd":"C:\\X"}""";
        var dto = JsonSerializer.Deserialize<CreateTabPayload>(json, Json)!;
        dto.Cols.Should().Be(120);
        dto.Rows.Should().Be(30);
        dto.ShellPath.Should().Be("powershell.exe");
        dto.Args.Should().Equal("-NoLogo");
        dto.Cwd.Should().Be(@"C:\X");
    }

    [Fact]
    public void CreateTab_OmitsOptionalFields_LeavesThemNull()
    {
        var dto = JsonSerializer.Deserialize<CreateTabPayload>("""{"cols":80,"rows":24}""", Json)!;
        dto.ShellPath.Should().BeNull();
        dto.Args.Should().BeNull();
        dto.Cwd.Should().BeNull();
    }

    [Fact]
    public void Input_RoundTrip()
    {
        var json = """{"tabId":"abc","dataB64":"aGVsbG8="}""";
        var dto = JsonSerializer.Deserialize<InputPayload>(json, Json)!;
        dto.TabId.Should().Be("abc");
        dto.DataB64.Should().Be("aGVsbG8=");
    }

    [Fact]
    public void Output_Serializes_WithCamelCase()
    {
        var msg = new OutputPayload("abc", "ZGF0YQ==");
        var json = JsonSerializer.Serialize(msg, Json);
        json.Should().Be("""{"tabId":"abc","dataB64":"ZGF0YQ=="}""");
    }

    [Fact]
    public void TabsList_Serializes()
    {
        var msg = new TabsListPayload(new[]
        {
            new SessionInfoPayload("id1", "powershell", "powershell.exe", @"C:\X", true)
        });
        var json = JsonSerializer.Serialize(msg, Json);
        json.Should().Contain("\"tabId\":\"id1\"");
        json.Should().Contain("\"alive\":true");
    }

    [Fact]
    public void SettingsPayload_HasNewMcpFields()
    {
        var p = new SettingsPayload(
            ShellPath: "pwsh", Args: Array.Empty<string>(),
            RingBufferKB: 1, XtermScrollbackLines: 1, Theme: "auto",
            AvailableShells: Array.Empty<ShellOptionPayload>(),
            McpEnabled: false, McpPort: 0, McpClients: Array.Empty<string>(),
            McpServerEnabled: true, McpServerPort: 7783,
            StudioProActionsEnabled: true, MaiaIntegrationEnabled: true,
            Platform: "windows",
            RefreshFromDiskHotkey: "F4", RestoreTabsOnReopen: true,
            About: new AboutInfoPayload("1.3.0", null, null),
            StudioProMcp: null);
        p.McpServerEnabled.Should().BeTrue();
        p.MaiaIntegrationEnabled.Should().BeTrue();
        p.Platform.Should().Be("windows");
    }
}
