using FluentAssertions;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Terminal;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class MaiaJsonRpcTests : IAsyncLifetime
{
    private sealed class FakeProbe : IRunStateProbe
    {
        public string? GetActiveUrl() => null;
        public int? GetActivePort() => null;
        public Task<RunState> IsRunningAsync(CancellationToken ct = default) => Task.FromResult(RunState.Stopped);
    }
    private sealed class FakeUi : IStudioProUiAutomation
    {
        public bool TriggerRun() => true;
        public bool TriggerStop() => true;
        public bool TriggerRefreshFromDisk() => true;
        public bool TriggerSaveAll() => true;
        public string? LastFailureReason => null;
    }
    private sealed class StubTransport : IMaiaTransport
    {
        public string Name => "stub";
        public int Tier => 1;
        public Task<HealthStatus> HealthCheckAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, 1, Name, 0));
        public Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
            => Task.FromResult(new SendResult(sentinel, sentinel, Name, DateTimeOffset.UtcNow));
        public Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
            => Task.FromResult(new StatusResult(true, "ok", false, 0, Name));
        public Task ResetAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private StudioProActionServer? server;
    private HttpClient http = null!;

    public async Task InitializeAsync()
    {
        var router = new MaiaRouter(new IMaiaTransport[] { new StubTransport() });
        await router.ProbeAllAsync(CancellationToken.None);
        var maia = new MaiaActions(router);
        var actions = new StudioProActions(new FakeProbe(), new FakeUi());
        server = new StudioProActionServer(
            actions, port: 0, log: null, maia: maia,
            studioProActionsEnabled: true, maiaIntegrationEnabled: true);
        server.Start();
        http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") };
    }

    public Task DisposeAsync() { server?.Dispose(); http.Dispose(); return Task.CompletedTask; }

    private async Task<JsonDocument> Post(string body)
    {
        using var resp = await http.PostAsync("/mcp", new StringContent(body, Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ToolsList_IncludesMaiaToolsWhenEnabled()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        var names = doc.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        names.Should().Contain(new[] {
            "maia__send", "maia__status", "maia__wait",
            "maia__ask", "maia__reset", "maia__force_tier"
        });
    }

    [Fact]
    public async Task ToolsCall_MaiaSend_ReturnsHandle()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"maia__send","arguments":{"prompt":"hi"}}}""");
        var text = doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        var inner = JsonDocument.Parse(text).RootElement;
        inner.GetProperty("status").GetString().Should().Be("sent");
    }
}
