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
}
