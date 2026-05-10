using System.Net.WebSockets;
using System.Text;
using FluentAssertions;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

/// <summary>
/// v4.2.1: unit-level coverage for CdpClient's reconnect-on-IsDisconnect
/// path. Manual smoke testing in v4.2.0 (CocktailDemo33 monitoring run,
/// 2026-05-10) proved the live behavior — these tests prevent regression
/// when the reconnect path is touched without re-running a full Studio Pro
/// session. The fake adapter simulates: drop after N successful evals,
/// successful reconnect, retry succeeds.
/// </summary>
public class CdpClientReconnectTests
{
    /// <summary>
    /// Test fake — implements <see cref="IWebSocketAdapter"/> well enough
    /// for the CDP request/response loop. Each instance represents ONE
    /// connection; a reconnect produces a fresh instance via the factory.
    /// Sends are buffered, receives surface a canned response (with the
    /// CDP id echoed back), and the next-receive failure mode is
    /// configurable.
    /// </summary>
    internal sealed class FakeWebSocketAdapter : IWebSocketAdapter
    {
        private readonly Queue<byte[]> outgoing = new();
        private bool open;
        private bool closed;
        public int SendsBeforeFailure { get; set; } = -1;   // -1 = never fail
        public int Sends { get; private set; }
        public int Receives { get; private set; }
        public Func<int, byte[]> ResponseFor { get; set; } = id =>
            Encoding.UTF8.GetBytes("{\"id\":" + id + ",\"result\":{\"result\":{\"value\":\"ok\"}}}");
        public bool DropOnReceive { get; set; } = false;

        public WebSocketState State => closed ? WebSocketState.Closed
            : open ? WebSocketState.Open
            : WebSocketState.None;

        public Task ConnectAsync(Uri uri, CancellationToken ct)
        {
            open = true;
            return Task.CompletedTask;
        }

        public Task SendAsync(ArraySegment<byte> buf, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
        {
            Sends++;
            if (SendsBeforeFailure >= 0 && Sends > SendsBeforeFailure)
            {
                open = false;
                throw new System.IO.IOException("simulated drop on send");
            }
            // Parse the id out of the request and queue a matching response.
            var json = Encoding.UTF8.GetString(buf.Array!, buf.Offset, buf.Count);
            var idMatch = System.Text.RegularExpressions.Regex.Match(json, "\"id\"\\s*:\\s*(\\d+)");
            int id = idMatch.Success && int.TryParse(idMatch.Groups[1].Value, out var n) ? n : 0;
            outgoing.Enqueue(ResponseFor(id));
            return Task.CompletedTask;
        }

        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buf, CancellationToken ct)
        {
            Receives++;
            if (DropOnReceive)
            {
                open = false;
                throw new System.IO.IOException("simulated drop on receive");
            }
            if (outgoing.Count == 0)
            {
                // No queued response — block until cancelled (or short-loop in tests).
                return Task.FromException<WebSocketReceiveResult>(
                    new System.IO.IOException("no response queued"));
            }
            var response = outgoing.Dequeue();
            var copy = Math.Min(response.Length, buf.Count);
            Array.Copy(response, 0, buf.Array!, buf.Offset, copy);
            return Task.FromResult(new WebSocketReceiveResult(copy, WebSocketMessageType.Text, true));
        }

        public Task CloseAsync(WebSocketCloseStatus status, string? statusDescription, CancellationToken ct)
        {
            closed = true;
            open = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            closed = true;
            open = false;
        }
    }

    private static (CdpClient client, List<FakeWebSocketAdapter> adapters) BuildClient()
    {
        var adapters = new List<FakeWebSocketAdapter>();
        Func<IWebSocketAdapter> factory = () =>
        {
            var a = new FakeWebSocketAdapter();
            adapters.Add(a);
            return a;
        };
        Func<CancellationToken, Task<(int port, string targetWsUrl)>> discovery =
            ct => Task.FromResult((9999, "ws://localhost:9999/devtools/page/test"));
        var client = new CdpClient(log: null, webSocketFactory: factory, discoveryOverride: discovery);
        return (client, adapters);
    }

    [Fact]
    public async Task ConnectMaiaAsync_UsesFactoryAndDiscoveryOverride()
    {
        var (client, adapters) = BuildClient();
        await client.ConnectMaiaAsync(CancellationToken.None);
        adapters.Count.Should().Be(1);
        adapters[0].State.Should().Be(WebSocketState.Open);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task EvaluateAsync_HappyPath_ReturnsValue()
    {
        var (client, _) = BuildClient();
        await client.ConnectMaiaAsync(CancellationToken.None);
        var result = await client.EvaluateAsync("return 1+1;", ct: CancellationToken.None);
        result.Should().NotBeNull();
        result!.GetValue<string>().Should().Be("ok");
        await client.DisposeAsync();
    }

    [Fact]
    public async Task EvaluateAsync_ReconnectsAfterIOExceptionOnSend()
    {
        // First adapter drops after 1 successful send; the wrapper's one-shot
        // reconnect path should spin up adapter #2 and retry the eval.
        var (client, adapters) = BuildClient();
        await client.ConnectMaiaAsync(CancellationToken.None);

        // First eval — adapter[0] handles cleanly.
        var first = await client.EvaluateAsync("return 1;", ct: CancellationToken.None);
        first!.GetValue<string>().Should().Be("ok");

        // Configure adapter[0] to drop on the NEXT send.
        adapters[0].SendsBeforeFailure = 1;

        // Second eval — should fail on adapter[0].SendAsync, EvaluateAsync's
        // catch-IsDisconnect branch reconnects (factory yields adapter[1]),
        // and the retry succeeds against adapter[1].
        var second = await client.EvaluateAsync("return 2;", ct: CancellationToken.None);
        second!.GetValue<string>().Should().Be("ok");

        adapters.Count.Should().Be(2, "reconnect should have allocated a fresh adapter");
        adapters[1].Sends.Should().BeGreaterThan(0, "retry should have used the second adapter");
        await client.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ClosesUnderlyingAdapter()
    {
        var (client, adapters) = BuildClient();
        await client.ConnectMaiaAsync(CancellationToken.None);
        await client.DisposeAsync();
        adapters[0].State.Should().Be(WebSocketState.Closed);
    }
}
