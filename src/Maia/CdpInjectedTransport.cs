using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Terminal.Maia;

public sealed class CdpInjectedTransport : IMaiaTransport
{
    public string Name => "cdp_injected";
    public int Tier => 1;

    private static readonly string AgentJs = LoadAgent();
    private readonly Func<ICdpClient> clientFactory;

    public CdpInjectedTransport(Func<ICdpClient> clientFactory)
    {
        this.clientFactory = clientFactory;
    }

    private static string LoadAgent()
    {
        using var s = typeof(CdpInjectedTransport).Assembly
            .GetManifestResourceStream("Terminal.Maia.maia_agent.js")
            ?? throw new InvalidOperationException("maia_agent.js resource missing from assembly");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public async Task<HealthStatus> HealthCheckAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // v4.2.0: clientFactory returns the singleton CdpClient owned by
            // the action-server. Do NOT `await using` — the singleton's
            // DisposeAsync would close the shared WebSocket and cancel the
            // heartbeat, defeating phase-1's connection-storm fix.
            var cdp = clientFactory();
            await cdp.ConnectMaiaAsync(ct);
            await EnsureAgentAsync(cdp, ct);
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
        await EnsureAgentAsync(cdp, ct);

        var js = $"return window.__maiaBridge.submit({JsonSerializer.Serialize(prompt)}, {JsonSerializer.Serialize(sentinel)});";
        var node = await cdp.EvaluateAsync(js, ct: ct);
        if (node is not JsonObject obj || obj["ok"]?.GetValue<bool>() != true)
            throw new TransportUnavailable($"submit() failed: {node?.ToJsonString()}");

        return new SendResult(
            Handle: sentinel,
            Sentinel: sentinel,
            TransportUsed: Name,
            SentAt: DateTimeOffset.UtcNow);
    }

    public async Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
    {
        var cdp = clientFactory();
        await cdp.ConnectMaiaAsync(ct);

        // First poll — fast path. The defensive JS wrapper returns
        // {__reinject: true} if window.__maiaBridge is missing or poll() throws.
        // On that signal we EnsureAgentAsync and retry once.
        var poll = await TryPollAsync(cdp, handle, ct);
        bool didReinject = false;
        if (poll.NeedsReinject)
        {
            await EnsureAgentAsync(cdp, ct);
            poll = await TryPollAsync(cdp, handle, ct);
            didReinject = true;
        }

        if (poll.UnknownHandle)
        {
            // Router translates this into Lost=true when the router itself
            // had previously bound the handle to this transport (per C6).
            // Genuinely-unknown handles (caller typo, never sent) keep this
            // shape so the router can throw the structured Unknown-handle
            // error. The didReinject flag is exposed via Lost on the result.
            return new StatusResult(
                Done: false, Response: "", Streaming: false,
                ElapsedSec: 0, TransportUsed: Name,
                Lost: didReinject,
                UnknownHandle: true);
        }
        if (poll.Result is not JsonObject p)
        {
            // Defensive last-ditch: re-injection didn't produce an object either.
            // Log the actual shape we saw — never use empty-string interpolation
            // (which is what produced "poll() returned unexpected shape: " in v4.1.4).
            var shape = poll.Result switch
            {
                null => "<null>",
                JsonValue v => $"value:{v.ToJsonString()}",
                JsonArray a => $"array(len={a.Count})",
                _ => $"node:{poll.Result.GetType().Name}"
            };
            throw new TransportUnavailable($"poll() returned non-object: {shape}");
        }

        var status = p["status"]?.GetValue<string>() ?? "pending";
        var elapsedMs = p["elapsed_ms"]?.GetValue<double>() ?? 0;
        return new StatusResult(
            Done: status == "done",
            Response: p["response"]?.GetValue<string>() ?? "",
            Streaming: status == "streaming",
            ElapsedSec: elapsedMs / 1000.0,
            TransportUsed: Name);
    }

    private readonly record struct PollOutcome(JsonNode? Result, bool NeedsReinject, bool UnknownHandle);

    private static async Task<PollOutcome> TryPollAsync(ICdpClient cdp, string handle, CancellationToken ct)
    {
        // Wrap the call in a try inside the JS so window.__maiaBridge being
        // missing surfaces as a structured value rather than a JS exception.
        // The IIFE in CdpClient.EvaluateAsync wraps this body; explicit
        // returns here surface a value to the C# side.
        var js = $$"""
            if (!window.__maiaBridge) return { __reinject: true, reason: 'no-bridge' };
            try {
                var r = window.__maiaBridge.poll({{JsonSerializer.Serialize(handle)}});
                return r;
            } catch (e) {
                return { __reinject: true, reason: 'poll-throw', message: String(e && e.message || e) };
            }
            """;
        var node = await cdp.EvaluateAsync(js, ct: ct);
        if (node is JsonObject obj)
        {
            if (obj["__reinject"]?.GetValue<bool>() == true)
                return new PollOutcome(node, NeedsReinject: true, UnknownHandle: false);
            if (obj["unknown"]?.GetValue<bool>() == true)
                return new PollOutcome(node, NeedsReinject: false, UnknownHandle: true);
            return new PollOutcome(node, NeedsReinject: false, UnknownHandle: false);
        }
        // null or non-object — treat as needs-reinject. The agent contract
        // says poll() always returns an object; if we got something else,
        // the agent is gone or corrupted.
        return new PollOutcome(node, NeedsReinject: true, UnknownHandle: false);
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        var cdp = clientFactory();
        try
        {
            await cdp.ConnectMaiaAsync(ct);
            await cdp.EvaluateAsync(
                "if (window.__maiaBridge) { window.__maiaBridge.teardown(); } return true;",
                ct: ct);
        }
        catch (TransportUnavailable) { /* nothing to clear */ }
    }

    private static async Task EnsureAgentAsync(ICdpClient cdp, CancellationToken ct)
    {
        var node = await cdp.EvaluateAsync(AgentJs, ct: ct);
        var v = node is JsonValue jv ? jv.GetValue<string>() : null;
        switch (v)
        {
            case "installed":
            case "already-installed":
                return;
            case "chat-root-not-found":
                throw new TransportUnavailable(
                    "Injected agent could not locate the chat-list container. Maia panel may not be fully rendered. Click the Maia tab and retry.");
            default:
                throw new TransportUnavailable($"Injected agent install returned unexpected result: {v ?? "<null>"}");
        }
    }
}
