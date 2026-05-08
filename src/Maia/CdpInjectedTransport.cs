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
            await using var cdp = clientFactory();
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
        await using var cdp = clientFactory();
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
        await using var cdp = clientFactory();
        await cdp.ConnectMaiaAsync(ct);
        var js = $"return window.__maiaBridge.poll({JsonSerializer.Serialize(handle)});";
        var node = await cdp.EvaluateAsync(js, ct: ct);
        if (node is not JsonObject p)
            throw new TransportUnavailable($"poll() returned unexpected shape: {node?.ToJsonString()}");

        if (p["unknown"]?.GetValue<bool>() == true)
            throw new TransportUnavailable($"Unknown handle: {handle}");

        var status = p["status"]?.GetValue<string>() ?? "pending";
        var elapsedMs = p["elapsed_ms"]?.GetValue<double>() ?? 0;
        return new StatusResult(
            Done: status == "done",
            Response: p["response"]?.GetValue<string>() ?? "",
            Streaming: status == "streaming",
            ElapsedSec: elapsedMs / 1000.0,
            TransportUsed: Name);
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        await using var cdp = clientFactory();
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
