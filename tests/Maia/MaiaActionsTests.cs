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

    /// <summary>
    /// v4.2.1: a stub that implements both transport + introspection so the
    /// new maia__busy / maia__new_chat code paths can be unit-tested without
    /// CDP. The introspection contract for production-shaped transports
    /// (CdpInjectedTransport) is exercised in CdpInjectedTransportTests.
    /// </summary>
    private sealed class IntrospectableStubTransport : IMaiaTransport, IMaiaIntrospection
    {
        public string Name => "stub-introspect";
        public int Tier => 1;
        public BusyResult? BusyFn;
        public NewChatResult? NewChatFn;
        public int ScanCount;
        public Task<HealthStatus> HealthCheckAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, 1, Name, 0));
        public Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
            => Task.FromResult(new SendResult(sentinel, sentinel, Name, DateTimeOffset.UtcNow));
        public Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
            => Task.FromResult(new StatusResult(true, "ok", false, 0.0, Name));
        public Task ResetAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<BusyResult> BusyAsync(CancellationToken ct)
            => Task.FromResult(BusyFn ?? new BusyResult(false, "idle", 12345));
        public Task<NewChatResult> NewChatAsync(CancellationToken ct)
            => Task.FromResult(NewChatFn ?? new NewChatResult(true, DateTimeOffset.UtcNow));
        public Task ScanAsync(CancellationToken ct) { ScanCount++; return Task.CompletedTask; }
    }

    private static MaiaActions Build(StubTransport t)
    {
        var router = new MaiaRouter(new IMaiaTransport[] { t });
        router.ProbeAllAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new MaiaActions(router);
    }

    private static (MaiaActions Actions, IntrospectableStubTransport Transport) BuildIntrospectable()
    {
        var t = new IntrospectableStubTransport();
        var router = new MaiaRouter(new IMaiaTransport[] { t });
        router.ProbeAllAsync(CancellationToken.None).GetAwaiter().GetResult();
        return (new MaiaActions(router), t);
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

    // ---- v4.2.1 introspection tests --------------------------------------

    [Fact]
    public async Task BusyAsync_FailsCleanlyWhenNoIntrospectionTransport()
    {
        var t = new StubTransport();   // no IMaiaIntrospection
        var a = Build(t);
        var r = await a.BusyAsync(CancellationToken.None);
        r.Error.Should().NotBeNull();
        r.Error!.Should().Contain("introspection");
    }

    [Fact]
    public async Task BusyAsync_ReturnsIdleSnapshotFromTransport()
    {
        var (a, t) = BuildIntrospectable();
        t.BusyFn = new BusyResult(false, "idle", 7777);
        var r = await a.BusyAsync(CancellationToken.None);
        r.Error.Should().BeNull();
        r.Status.Should().Be("idle");
        var data = (System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            System.Text.Json.JsonSerializer.Serialize(r.Data)))!;
        data["busy"]!.GetValue<bool>().Should().BeFalse();
        data["reason"]!.GetValue<string>().Should().Be("idle");
        data["idle_for_ms"]!.GetValue<long>().Should().Be(7777);
    }

    [Fact]
    public async Task BusyAsync_ReturnsBusyWhenSpinnerVisible()
    {
        var (a, t) = BuildIntrospectable();
        t.BusyFn = new BusyResult(true, "spinner-visible", 42, Spinner: "[role=\"progressbar\"]");
        var r = await a.BusyAsync(CancellationToken.None);
        r.Status.Should().Be("busy");
        var data = (System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            System.Text.Json.JsonSerializer.Serialize(r.Data)))!;
        data["busy"]!.GetValue<bool>().Should().BeTrue();
        data["reason"]!.GetValue<string>().Should().Be("spinner-visible");
        data["spinner"]!.GetValue<string>().Should().Contain("progressbar");
    }

    [Fact]
    public async Task NewChatAsync_PropagatesTransportFailure()
    {
        var (a, t) = BuildIntrospectable();
        t.NewChatFn = new NewChatResult(false, Error: "new-chat-button-not-found");
        var r = await a.NewChatAsync(CancellationToken.None);
        r.Error.Should().NotBeNull();
        r.Error!.Should().Contain("new-chat-button-not-found");
    }

    [Fact]
    public async Task NewChatAsync_OkOnSuccess()
    {
        var (a, t) = BuildIntrospectable();
        t.NewChatFn = new NewChatResult(true, DateTimeOffset.UtcNow);
        var r = await a.NewChatAsync(CancellationToken.None);
        r.Error.Should().BeNull();
        r.Status.Should().Be("new_chat_started");
    }

    [Fact]
    public async Task HealthAsync_IncludesTransportSnapshotAndBusyEmbedding()
    {
        var (a, t) = BuildIntrospectable();
        t.BusyFn = new BusyResult(false, "idle", 1234);
        var r = await a.HealthAsync(CancellationToken.None);
        r.Error.Should().BeNull();
        r.Status.Should().Be("health");
        var data = (System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            System.Text.Json.JsonSerializer.Serialize(r.Data)))!;
        data["transports"].Should().NotBeNull();
        var tps = (System.Text.Json.Nodes.JsonArray)data["transports"]!;
        tps.Count.Should().Be(1);
        tps[0]!["name"]!.GetValue<string>().Should().Be("stub-introspect");
        tps[0]!["available"]!.GetValue<bool>().Should().BeTrue();
        data["maia_busy"]!["reason"]!.GetValue<string>().Should().Be("idle");
        data["active_bindings"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task HealthAsync_NoIntrospectionTransport_StillReturnsRouterState()
    {
        var t = new StubTransport();
        var a = Build(t);
        var r = await a.HealthAsync(CancellationToken.None);
        r.Error.Should().BeNull();
        var data = (System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            System.Text.Json.JsonSerializer.Serialize(r.Data)))!;
        // No introspection → maia_busy is null but transports/last-probe are present.
        data["transports"].Should().NotBeNull();
        ((System.Text.Json.Nodes.JsonArray)data["transports"]!).Count.Should().Be(1);
        // maia_busy is serialized as "null" in JSON; missing key is also acceptable.
        if (data.ContainsKey("maia_busy"))
            data["maia_busy"]?.AsObject().Should().BeNull();
    }

    [Fact]
    public async Task PingAsync_ReturnsAliveWhenWaitDone()
    {
        var t = new StubTransport
        {
            // Stub returns done=true on first poll → wait completes immediately.
            StatusFn = (h, _) => new StatusResult(true, "pong", false, 0.05, "stub"),
        };
        var a = Build(t);
        var r = await a.PingAsync(timeoutSec: 5.0, CancellationToken.None);
        r.Error.Should().BeNull();
        r.Status.Should().Be("alive");
        var data = (System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            System.Text.Json.JsonSerializer.Serialize(r.Data)))!;
        data["alive"]!.GetValue<bool>().Should().BeTrue();
        data["latency_ms"]!.GetValue<long>().Should().BeGreaterOrEqualTo(0);
        data["timed_out"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task PingAsync_ReturnsTimedOutWhenWaitNeverDone()
    {
        var t = new StubTransport
        {
            StatusFn = (h, _) => new StatusResult(false, "", false, 0.0, "stub"),
        };
        var a = Build(t);
        var r = await a.PingAsync(timeoutSec: 0.3, CancellationToken.None);
        r.Status.Should().Be("no_response");
        var data = (System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            System.Text.Json.JsonSerializer.Serialize(r.Data)))!;
        data["alive"]!.GetValue<bool>().Should().BeFalse();
        data["timed_out"]!.GetValue<bool>().Should().BeTrue();
    }
}
