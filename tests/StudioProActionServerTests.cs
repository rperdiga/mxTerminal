using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class StudioProActionServerTests : IAsyncLifetime
{
    private sealed class FakeProbe : IRunStateProbe
    {
        public string? Url { get; set; } = "http://localhost:8080";
        public int? Port { get; set; } = 8080;
        public Func<RunState> Next = () => RunState.Stopped;
        public string? GetActiveUrl() => Url;
        public int? GetActivePort() => Port;
        public Task<RunState> IsRunningAsync(CancellationToken ct = default) => Task.FromResult(Next());
    }

    private sealed class FakeUi : IStudioProUiAutomation
    {
        public bool TriggerRun() => true;
        public bool TriggerStop() => true;
        public bool TriggerRefreshFromDisk() => true;
        public bool TriggerSaveAll() => true;
        public string? LastFailureReason => null;
    }

    private StudioProActionServer? server;
    private HttpClient http = null!;

    public Task InitializeAsync()
    {
        var probe = new FakeProbe { Next = () => RunState.Running };
        var actions = new StudioProActions(probe, new FakeUi(),
            runTimeout: TimeSpan.FromMilliseconds(200),
            stopTimeout: TimeSpan.FromMilliseconds(200),
            pollInterval: TimeSpan.FromMilliseconds(50));
        server = new StudioProActionServer(actions, port: 0);  // ephemeral
        server.Start();
        http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") };
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        server?.Dispose();
        http.Dispose();
        return Task.CompletedTask;
    }

    private async Task<JsonDocument> Post(string body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        var info = doc.RootElement.GetProperty("result").GetProperty("serverInfo");
        info.GetProperty("name").GetString().Should().Be("concord-mcp");
        info.GetProperty("version").GetString().Should().StartWith("1.3");
    }

    [Fact]
    public async Task ToolsList_ReturnsExpectedTools()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        var names = new List<string>();
        foreach (var t in tools.EnumerateArray()) names.Add(t.GetProperty("name").GetString()!);
        // Slice 2 expanded the tool surface from 3 (run/stop/refresh) to 6.
        names.Should().BeEquivalentTo(new[] {
            "run_app", "stop_app", "refresh_project",
            "save_all", "get_active_run_configuration", "get_app_status",
        });
    }

    [Fact]
    public async Task ToolsCall_RunApp_ReturnsAlreadyRunningResult()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"run_app","arguments":{}}}""");
        // MCP tools/call result: { content: [ { type: "text", text: "<json>" } ] }
        var content = doc.RootElement.GetProperty("result").GetProperty("content");
        content.GetArrayLength().Should().BeGreaterThan(0);
        var text = content[0].GetProperty("text").GetString()!;
        var inner = JsonDocument.Parse(text).RootElement;
        inner.GetProperty("status").GetString().Should().Be("already_running");
        inner.GetProperty("url").GetString().Should().Be("http://localhost:8080");
    }

    [Fact]
    public async Task ToolsCall_UnknownTool_ReturnsMcpError()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"nope","arguments":{}}}""");
        doc.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601);
    }

    [Fact]
    public async Task MalformedJson_ReturnsParseError()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{ not json", Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var resp = await http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(text);
        doc.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32700);
    }

    [Fact]
    public void Port_AvailableAfterStart()
    {
        // Sanity: Port property exposes the bound port. The listener prefix is http://127.0.0.1:{port}/
        // so the bind is loopback-only by definition.
        server!.Port.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DoubleStart_Throws()
    {
        Action act = () => server!.Start();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task DisposeStopsListener()
    {
        var probe = new FakeProbe();
        var actions = new StudioProActions(probe, new FakeUi());
        var s = new StudioProActionServer(actions, port: 0);
        s.Start();
        var port = s.Port;
        s.Dispose();
        // After dispose, attempt to connect should fail.
        using var c = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromMilliseconds(500) };
        Func<Task> call = async () => await c.GetAsync("/mcp");
        await call.Should().ThrowAsync<Exception>().Where(e =>
            e is HttpRequestException || e is OperationCanceledException || e is TaskCanceledException);
    }
}
