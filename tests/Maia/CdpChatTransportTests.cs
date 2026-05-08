using FluentAssertions;
using System.Text.Json.Nodes;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class CdpChatTransportTests
{
    private sealed class FakeCdp : ICdpClient
    {
        public List<string> Evals { get; } = new();
        public Func<string, JsonNode?>? Responder;
        public Task ConnectMaiaAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<JsonNode?> EvaluateAsync(string js, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            Evals.Add(js);
            return Task.FromResult(Responder?.Invoke(js));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task SendAsync_TypesIntoInputAndDispatchesEnter()
    {
        var fake = new FakeCdp { Responder = _ => JsonValue.Create(true) };
        var t = new CdpChatTransport(() => fake);

        var r = await t.SendAsync("hello", "<MX-T2>", CancellationToken.None);

        fake.Evals.Should().Contain(s => s.Contains("MX_CHAT_INPUT"));
        fake.Evals.Should().Contain(s => s.Contains("Enter"));
        r.TransportUsed.Should().Be("cdp_chat");
    }

    [Fact]
    public async Task StatusAsync_DetectsTwoSentinelsAsDone()
    {
        var bubbles = new JsonArray
        {
            "user echo: please answer <MX-T2>",
            "Maia: answered.",
            "Maia echo: <MX-T2>",
        };
        int call = 0;
        var fake = new FakeCdp
        {
            // First call (SendAsync's typing eval) returns true; subsequent calls
            // (StatusAsync's bubble query) return the bubble array. This pre-registers
            // the handle via SendAsync so StatusAsync doesn't trip the unknown-handle guard.
            Responder = _ => ++call == 1 ? (JsonNode)JsonValue.Create(true) : bubbles
        };
        var t = new CdpChatTransport(() => fake);
        await t.SendAsync("dummy", "<MX-T2>", CancellationToken.None);

        var s = await t.StatusAsync("<MX-T2>", CancellationToken.None);

        s.Done.Should().BeTrue();
        s.Response.Should().Contain("answered");
    }
}
