using FluentAssertions;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class MaiaRouterTests
{
    private sealed class FakeTransport : IMaiaTransport
    {
        public string Name { get; }
        public int Tier { get; }
        public bool Available { get; set; } = true;
        public bool ThrowUnavailableOnSend { get; set; }
        public int SendCalls;
        public int ResetCalls;
        public int HealthCalls;

        public FakeTransport(string name, int tier) { Name = name; Tier = tier; }

        public Task<HealthStatus> HealthCheckAsync(CancellationToken ct)
        {
            HealthCalls++;
            return Task.FromResult(Available
                ? new HealthStatus(true, Tier, Name, 1.0)
                : new HealthStatus(false, Tier, Name, 1.0, "fake offline"));
        }
        public Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
        {
            SendCalls++;
            if (ThrowUnavailableOnSend) throw new TransportUnavailable("fake send unavailable");
            return Task.FromResult(new SendResult(sentinel, sentinel, Name, DateTimeOffset.UtcNow));
        }
        public Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
            => Task.FromResult(new StatusResult(true, "ok", false, 0.1, Name));
        public Task ResetAsync(CancellationToken ct) { ResetCalls++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task LowestTierAvailable_IsPickedAtStartup()
    {
        var t1 = new FakeTransport("t1", 1) { Available = false };
        var t2 = new FakeTransport("t2", 2);
        var router = new MaiaRouter(new IMaiaTransport[] { t1, t2 });
        await router.ProbeAllAsync(CancellationToken.None);

        var r = await router.SendAsync("p", "<MX-X>", CancellationToken.None);

        r.TransportUsed.Should().Be("t2");
    }

    [Fact]
    public async Task PerCallUnavailable_DemotesAndRetries()
    {
        var t1 = new FakeTransport("t1", 1) { ThrowUnavailableOnSend = true };
        var t2 = new FakeTransport("t2", 2);
        var router = new MaiaRouter(new IMaiaTransport[] { t1, t2 });
        await router.ProbeAllAsync(CancellationToken.None);

        var r = await router.SendAsync("p", "<MX-X>", CancellationToken.None);

        t1.SendCalls.Should().Be(1);
        t2.SendCalls.Should().Be(1);
        r.TransportUsed.Should().Be("t2");
    }

    [Fact]
    public async Task AllExhausted_RaisesUnavailable()
    {
        var t1 = new FakeTransport("t1", 1) { ThrowUnavailableOnSend = true };
        var t2 = new FakeTransport("t2", 2) { ThrowUnavailableOnSend = true };
        var router = new MaiaRouter(new IMaiaTransport[] { t1, t2 });
        await router.ProbeAllAsync(CancellationToken.None);

        Func<Task> act = () => router.SendAsync("p", "<MX-X>", CancellationToken.None);

        await act.Should().ThrowAsync<TransportUnavailable>()
            .Where(e => e.Message.Contains("All Maia transports unavailable"));
    }

    [Fact]
    public async Task Reset_CallsEveryTransport()
    {
        var t1 = new FakeTransport("t1", 1);
        var t2 = new FakeTransport("t2", 2);
        var router = new MaiaRouter(new IMaiaTransport[] { t1, t2 });
        await router.ProbeAllAsync(CancellationToken.None);

        await router.ResetAsync(CancellationToken.None);

        t1.ResetCalls.Should().Be(1);
        t2.ResetCalls.Should().Be(1);
    }

    [Fact]
    public async Task ForceTier_OverridesActiveUntilReprobe()
    {
        var t1 = new FakeTransport("t1", 1);
        var t2 = new FakeTransport("t2", 2);
        var router = new MaiaRouter(new IMaiaTransport[] { t1, t2 });
        await router.ProbeAllAsync(CancellationToken.None);

        router.ForceTier("t2");
        var r = await router.SendAsync("p", "<MX-X>", CancellationToken.None);

        r.TransportUsed.Should().Be("t2");
    }

    [Fact]
    public void ForceTier_UnknownName_Throws()
    {
        var t1 = new FakeTransport("t1", 1);
        var router = new MaiaRouter(new IMaiaTransport[] { t1 });

        Action act = () => router.ForceTier("nope");

        act.Should().Throw<ArgumentException>().WithMessage("*nope*");
    }
}
