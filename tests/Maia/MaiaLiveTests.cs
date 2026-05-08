using FluentAssertions;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

[Trait("Category", "MaiaLive")]
public class MaiaLiveTests
{
    private static bool LiveEnabled =>
        Environment.GetEnvironmentVariable("CONCORD_MAIA_LIVE") == "1";

    private static MaiaRouter NewRouter()
    {
        var transports = new IMaiaTransport[]
        {
            new CdpInjectedTransport(() => new CdpClient()),
            new CdpChatTransport(() => new CdpClient()),
        };
        var r = new MaiaRouter(transports);
        r.ProbeAllAsync(CancellationToken.None).GetAwaiter().GetResult();
        return r;
    }

    [SkippableFact]
    public async Task HealthProbe_Tier1_IsActive_WhenMaiaPanelOpen()
    {
        Skip.IfNot(LiveEnabled);
        var router = NewRouter();
        var t1 = router.Transports.First(t => t.Name == "cdp_injected");
        var h = await t1.HealthCheckAsync(CancellationToken.None);
        h.Available.Should().BeTrue($"reason: {h.Reason}");
    }

    [SkippableFact]
    public async Task Ask_MpmExtension_ReturnsExpectedAnswer()
    {
        Skip.IfNot(LiveEnabled);
        var actions = new MaiaActions(NewRouter());
        var r = await actions.AskAsync(
            "In ten words, what file extension do Mendix project files use?",
            timeoutSec: 30, CancellationToken.None);
        r.Error.Should().BeNull();
        var json = System.Text.Json.JsonSerializer.Serialize(r.Data);
        json.Should().ContainEquivalentOf(".mpr");
    }

    [SkippableFact]
    public async Task ForceTier_CdpChat_AnswersCorrectly()
    {
        Skip.IfNot(LiveEnabled);
        var router = NewRouter();
        router.ForceTier("cdp_chat");
        var actions = new MaiaActions(router);
        var r = await actions.AskAsync(
            "Reply with the single word: pong",
            timeoutSec: 30, CancellationToken.None);
        r.Error.Should().BeNull();
    }
}
