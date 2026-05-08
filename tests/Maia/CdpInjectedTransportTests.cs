using FluentAssertions;
using System.Text.Json.Nodes;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class CdpInjectedTransportTests
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
    public async Task SendAsync_InjectsAgentThenSubmits()
    {
        var fake = new FakeCdp
        {
            Responder = js =>
                js.Contains("window.__maiaBridge") && js.Contains("findChatRoot")
                    ? JsonValue.Create("installed")
                    : new JsonObject { ["ok"] = true }
        };
        var t = new CdpInjectedTransport(() => fake);

        var r = await t.SendAsync("hi", "<MX-TEST>", CancellationToken.None);

        fake.Evals[0].Should().Contain("window.__maiaBridge"); // agent install
        fake.Evals[1].Should().Contain("window.__maiaBridge.submit");
        r.Sentinel.Should().Be("<MX-TEST>");
        r.TransportUsed.Should().Be("cdp_injected");
    }

    [Fact]
    public async Task SendAsync_AgentReturnsChatRootNotFound_RaisesUnavailable()
    {
        var fake = new FakeCdp { Responder = _ => JsonValue.Create("chat-root-not-found") };
        var t = new CdpInjectedTransport(() => fake);

        Func<Task> act = () => t.SendAsync("hi", "<MX-TEST>", CancellationToken.None);

        await act.Should().ThrowAsync<TransportUnavailable>()
            .Where(e => e.Message.Contains("chat-list container"));
    }
}
