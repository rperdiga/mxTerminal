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
        public int ConnectCalls;
        public Task ConnectMaiaAsync(CancellationToken ct)
        {
            ConnectCalls++;
            return Task.CompletedTask;
        }
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

    // v4.2.0: factory must be invoked (proves the singleton wiring path
    // is exercised at the transport boundary). With a real persistent
    // CdpClient the second ConnectMaiaAsync is a no-op; with the fake the
    // counter just confirms both call sites went through the factory.
    [Fact]
    public async Task SendThenStatus_BothCallSitesInvokeFactory()
    {
        int pollCount = 0;
        var fake = new FakeCdp
        {
            Responder = js =>
            {
                if (js.Contains("findChatRoot")) return JsonValue.Create("installed");
                if (js.Contains("submit")) return new JsonObject { ["ok"] = true };
                if (js.Contains("__maiaBridge.poll"))
                {
                    pollCount++;
                    return new JsonObject
                    {
                        ["status"] = "pending",
                        ["response"] = "",
                        ["elapsed_ms"] = 100.0,
                    };
                }
                return null;
            }
        };
        var t = new CdpInjectedTransport(() => fake);
        await t.SendAsync("hi", "<MX-T>", CancellationToken.None);
        await t.StatusAsync("<MX-T>", CancellationToken.None);

        fake.ConnectCalls.Should().Be(2);
        pollCount.Should().Be(1);
    }

    // v4.2.0 P3: when window.__maiaBridge is missing, the wrapper returns
    // {__reinject:true}; transport must re-inject and retry once.
    [Fact]
    public async Task StatusAsync_BridgeMissing_ReInjectsAndRetries()
    {
        bool agentInstalled = false;
        int evalCount = 0;
        var fake = new FakeCdp
        {
            Responder = js =>
            {
                evalCount++;
                if (js.Contains("findChatRoot"))
                {
                    agentInstalled = true;
                    return JsonValue.Create("installed");
                }
                if (js.Contains("__maiaBridge.poll"))
                {
                    if (!agentInstalled)
                        return new JsonObject { ["__reinject"] = true, ["reason"] = "no-bridge" };
                    return new JsonObject
                    {
                        ["status"] = "done",
                        ["response"] = "ok",
                        ["elapsed_ms"] = 50.0,
                    };
                }
                return null;
            }
        };
        var t = new CdpInjectedTransport(() => fake);

        var s = await t.StatusAsync("<MX-T>", CancellationToken.None);

        s.Done.Should().BeTrue();
        s.Response.Should().Be("ok");
        // Three evals: first poll (re-inject signal) → agent install → second poll.
        evalCount.Should().Be(3);
    }

    // v4.2.0 P3: defensive parser surfaces the actual shape it saw,
    // never the empty-string interpolation that v4.1.4 produced.
    [Fact]
    public async Task StatusAsync_NonObjectResult_LogsActualShape()
    {
        bool agentInstalled = false;
        var fake = new FakeCdp
        {
            Responder = js =>
            {
                if (js.Contains("findChatRoot"))
                {
                    agentInstalled = true;
                    return JsonValue.Create("installed");
                }
                if (js.Contains("__maiaBridge.poll"))
                {
                    // Always return a non-object — re-inject won't help, so
                    // the defensive last-ditch branch fires.
                    return JsonValue.Create(42);
                }
                return null;
            }
        };
        var t = new CdpInjectedTransport(() => fake);

        Func<Task> act = () => t.StatusAsync("<MX-T>", CancellationToken.None);

        await act.Should().ThrowAsync<TransportUnavailable>()
            .Where(e => e.Message.Contains("poll() returned non-object")
                     && e.Message.Contains("value:42"));
        // Sanity: agent did get installed during the re-inject attempt.
        agentInstalled.Should().BeTrue();
    }

    // v4.2.0 P3+P4: re-inject + still-unknown returns Lost=true (router
    // translates to "lost" if it had a binding, throws "Unknown handle"
    // otherwise — see MaiaRouterTests for that branch).
    [Fact]
    public async Task StatusAsync_UnknownAfterReinject_ReturnsLostFlag()
    {
        int pollCount = 0;
        var fake = new FakeCdp
        {
            Responder = js =>
            {
                if (js.Contains("findChatRoot")) return JsonValue.Create("installed");
                if (js.Contains("__maiaBridge.poll"))
                {
                    pollCount++;
                    if (pollCount == 1) return new JsonObject { ["__reinject"] = true };
                    return new JsonObject { ["unknown"] = true };
                }
                return null;
            }
        };
        var t = new CdpInjectedTransport(() => fake);

        var s = await t.StatusAsync("<MX-LOST>", CancellationToken.None);

        s.Lost.Should().BeTrue();
        s.UnknownHandle.Should().BeTrue();
        s.Done.Should().BeFalse();
    }

    // v4.2.0 P3: genuine unknown on first poll (no reinject needed) — the
    // transport still returns UnknownHandle so the router can decide
    // throw-vs-Lost based on its own bindings.
    [Fact]
    public async Task StatusAsync_UnknownOnFirstPoll_NoReinject()
    {
        var fake = new FakeCdp
        {
            Responder = js =>
                js.Contains("__maiaBridge.poll")
                    ? new JsonObject { ["unknown"] = true }
                    : null
        };
        var t = new CdpInjectedTransport(() => fake);

        var s = await t.StatusAsync("<MX-NEVERSEEN>", CancellationToken.None);

        s.UnknownHandle.Should().BeTrue();
        s.Lost.Should().BeFalse();   // didReinject was false
    }
}
