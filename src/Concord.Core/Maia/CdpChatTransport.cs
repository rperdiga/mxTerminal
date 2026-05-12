using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Terminal.Maia;

/// <summary>
/// Tier 2: drives Maia by typing into #MX_CHAT_INPUT, dispatching Enter, and
/// scraping bubble innerText. Brittle to Mendix's generated CSS class
/// (p.sc-bPkUNa) — when that regenerates per Mendix build, this transport
/// breaks but Tier 1's structural walk keeps working.
/// </summary>
public sealed class CdpChatTransport : IMaiaTransport
{
    public string Name => "cdp_chat";
    public int Tier => 2;

    private const string BubbleSelector = "p.sc-bPkUNa";

    private readonly Func<ICdpClient> clientFactory;
    private readonly Dictionary<string, DateTimeOffset> tickets = new();
    private readonly object gate = new();

    public CdpChatTransport(Func<ICdpClient> clientFactory)
    {
        this.clientFactory = clientFactory;
    }

    public async Task<HealthStatus> HealthCheckAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // v4.2.0: clientFactory returns the singleton CdpClient — do NOT
            // `await using`, the singleton's DisposeAsync would close the
            // shared WebSocket and cancel the heartbeat.
            var cdp = clientFactory();
            await cdp.ConnectMaiaAsync(ct);
            var node = await cdp.EvaluateAsync(
                "return !!document.getElementById('MX_CHAT_INPUT');", ct: ct);
            if (node?.GetValue<bool>() != true)
                throw new TransportUnavailable("MX_CHAT_INPUT not found in Maia panel.");
            return new HealthStatus(true, Tier, Name, sw.Elapsed.TotalMilliseconds);
        }
        catch (TransportUnavailable ex)
        {
            return new HealthStatus(false, Tier, Name, sw.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    public async Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
    {
        var cdp = clientFactory();
        await cdp.ConnectMaiaAsync(ct);
        var folded = string.Join(' ', prompt.Split('\n', '\r', '\t').Where(s => s.Length > 0));
        var full = $"{folded} Respond, then write {sentinel} on a new line so the pipeline knows you are done.";
        var js = $$"""
            const ta = document.getElementById('MX_CHAT_INPUT');
            if (!ta) return false;
            const setter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;
            setter.call(ta, {{JsonSerializer.Serialize(full)}});
            ta.dispatchEvent(new Event('input', { bubbles: true }));
            ta.focus();
            ta.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true }));
            return true;
            """;
        var ok = await cdp.EvaluateAsync(js, ct: ct);
        if (ok?.GetValue<bool>() != true)
            throw new TransportUnavailable("Could not type into MX_CHAT_INPUT.");

        lock (gate) tickets[sentinel] = DateTimeOffset.UtcNow;

        return new SendResult(
            Handle: sentinel,
            Sentinel: sentinel,
            TransportUsed: Name,
            SentAt: DateTimeOffset.UtcNow);
    }

    public async Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
    {
        DateTimeOffset sentAt;
        lock (gate)
        {
            if (!tickets.TryGetValue(handle, out sentAt))
            {
                // v4.2.0: surface UnknownHandle instead of throwing so the
                // router can decide lost-vs-unknown using its own bindings.
                return new StatusResult(
                    Done: false, Response: "", Streaming: false,
                    ElapsedSec: 0, TransportUsed: Name, UnknownHandle: true);
            }
        }

        var cdp = clientFactory();
        await cdp.ConnectMaiaAsync(ct);
        var js = $$"""
            return [...document.querySelectorAll({{JsonSerializer.Serialize(BubbleSelector)}})]
                .map(p => p.innerText || '');
            """;
        var node = await cdp.EvaluateAsync(js, ct: ct);
        var bubbles = (node as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").ToList()
            ?? throw new TransportUnavailable("Bubble selector returned non-array.");

        int firstIdx = -1, lastIdx = -1;
        for (int i = 0; i < bubbles.Count; i++)
        {
            if (bubbles[i].Contains(handle))
            {
                if (firstIdx == -1) firstIdx = i;
                lastIdx = i;
            }
        }
        var elapsed = (DateTimeOffset.UtcNow - sentAt).TotalSeconds;
        if (firstIdx == -1)
            return new StatusResult(false, "", false, elapsed, Name);

        bool done = lastIdx > firstIdx;
        int start = firstIdx + 1;
        int end = done ? lastIdx : bubbles.Count;
        var reply = string.Join('\n', bubbles.Skip(start).Take(end - start)).Replace(handle, "").Trim();
        return new StatusResult(done, reply, !done, elapsed, Name);
    }

    public Task ResetAsync(CancellationToken ct)
    {
        lock (gate) tickets.Clear();
        return Task.CompletedTask;
    }
}
