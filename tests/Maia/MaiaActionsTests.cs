using FluentAssertions;
using Terminal;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class MaiaActionsTests
{
    private sealed class StubTransport : IMaiaTransport
    {
        public string Name => "stub";
        public int Tier => 1;
        public Func<string, string, StatusResult>? StatusFn;
        public int Sends;
        public Task<HealthStatus> HealthCheckAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, 1, Name, 0));
        public Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
        {
            Sends++;
            return Task.FromResult(new SendResult(sentinel, sentinel, Name, DateTimeOffset.UtcNow));
        }
        public Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
            => Task.FromResult(StatusFn?.Invoke(handle, "") ?? new StatusResult(true, "ok", false, 0.0, Name));
        public Task ResetAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static MaiaActions Build(StubTransport t)
    {
        var router = new MaiaRouter(new IMaiaTransport[] { t });
        router.ProbeAllAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new MaiaActions(router);
    }

    [Fact]
    public async Task SendAsync_GeneratesSentinelWhenOmitted()
    {
        var t = new StubTransport();
        var a = Build(t);
        var r = await a.SendAsync("hi", null, CancellationToken.None);
        r.Status.Should().Be("sent");
        var data = (System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            System.Text.Json.JsonSerializer.Serialize(r.Data)))!;
        data["sentinel"]!.GetValue<string>().Should().StartWith("<MX-");
    }

    [Fact]
    public async Task WaitAsync_ReturnsTimedOutWhenNeverDone()
    {
        var t = new StubTransport
        {
            StatusFn = (h, _) => new StatusResult(false, "", false, 0.0, "stub"),
        };
        var a = Build(t);
        var send = await a.SendAsync("hi", "<MX-T>", CancellationToken.None);
        var w = await a.WaitAsync("<MX-T>", timeoutSec: 0.5, CancellationToken.None);

        var data = (System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            System.Text.Json.JsonSerializer.Serialize(w.Data)))!;
        data["timed_out"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task ForceTierAsync_RejectsUnknownName()
    {
        var t = new StubTransport();
        var a = Build(t);
        var r = await a.ForceTierAsync("doesnt-exist", CancellationToken.None);
        r.Error.Should().NotBeNull();
        r.Error!.Should().Contain("doesnt-exist");
    }
}
