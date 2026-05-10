using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Net.Http;

namespace Terminal.Maia;

/// <summary>
/// Cross-platform-aware: on non-Windows, ConnectMaiaAsync immediately raises
/// TransportUnavailable so the router reports zero tiers. The main implementation
/// is Windows + PowerShell-CIM + WebSocket. (System.Management cannot be used
/// directly because Concord targets net8.0 — System.Management requires
/// net8.0-windows, and we can't change TFM without breaking macOS support.
/// Shelling out to powershell.exe is what the Python prototype did too.)
///
/// v4.2.0+: instances are intended to be long-lived. ConnectMaiaAsync is
/// idempotent under a connect gate — first call performs port discovery and
/// the WebSocket handshake; subsequent calls return immediately when the
/// underlying socket is healthy. EvaluateAsync wraps EvaluateOnceAsync with
/// a one-shot reconnect when the WebSocket drops mid-call. Discovery results
/// (port + targetWsUrl) are cached and invalidated on connect failure.
/// Callers MUST NOT `await using var cdp = clientFactory()` — the singleton
/// is owned by the action-server lifecycle.
/// </summary>
public sealed class CdpClient : ICdpClient
{
    private const string TargetUrlSubstr = "maia-agent";
    private static readonly TimeSpan EvaluateDefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private ClientWebSocket? ws;
    private int messageId;
    private readonly SemaphoreSlim evalGate = new(1, 1);
    private readonly SemaphoreSlim connectGate = new(1, 1);

    private int? cachedPort;
    private string? cachedTargetWsUrl;
    private DateTime cachedDiscoveryAt;

    private readonly Logger? log;

    public CdpClient() : this(null) { }
    public CdpClient(Logger? log) { this.log = log; }

    public async Task ConnectMaiaAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new TransportUnavailable("Maia bridge is Windows-only in this Concord release.");

        await connectGate.WaitAsync(ct);
        try
        {
            if (ws is { State: WebSocketState.Open }) return;   // already healthy
            await ConnectInternalAsync(ct);
        }
        finally { connectGate.Release(); }
    }

    private async Task ConnectInternalAsync(CancellationToken ct)
    {
        // Reuse cached discovery when present; the WMI scan and /json GET are
        // the expensive parts. Cache is invalidated on connect failure below
        // so a Studio Pro restart self-heals after one failed call.
        int port;
        string targetWsUrl;
        if (cachedPort is int p && cachedTargetWsUrl is string url)
        {
            port = p;
            targetWsUrl = url;
            log?.Debug($"[cdp] reusing cached discovery port={port}");
        }
        else
        {
            port = FindDebugPort();
            targetWsUrl = await DiscoverMaiaTargetAsync(port, ct);
            cachedPort = port;
            cachedTargetWsUrl = targetWsUrl;
            cachedDiscoveryAt = DateTime.UtcNow;
            log?.Debug($"[cdp] discovered port={port}");
        }

        // Tear down any half-open socket from a previous failed attempt.
        if (ws is not null)
        {
            try { ws.Dispose(); } catch { }
            ws = null;
        }

        ws = new ClientWebSocket();
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(5));
            await ws.ConnectAsync(new Uri(targetWsUrl), connectCts.Token);
            log?.Debug($"[cdp] websocket connected port={port}");
        }
        catch (Exception ex) when (ex is not TransportUnavailable)
        {
            try { ws.Dispose(); } catch { }
            ws = null;
            // Discovery may have gone stale (Studio Pro restarted on a different
            // port). Invalidate cache so the next call re-probes.
            cachedPort = null;
            cachedTargetWsUrl = null;
            throw new TransportUnavailable($"WebSocket connect to Maia target failed: {ex.Message}", ex)
            {
                IsDisconnect = true,
            };
        }
    }

    private static int FindDebugPort()
    {
        // Shell out to Windows PowerShell for the CIM query. Why not System.Management
        // directly: that package requires net8.0-windows TFM, and Concord targets
        // plain net8.0 so the same DLL can load on macOS too. On net8.0 the
        // package's stub throws PlatformNotSupportedException at every call site.
        // PowerShell's Get-CimInstance does the WMI lookup in its own process,
        // sidestepping the TFM constraint entirely. Same approach the Python
        // prototype used.
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // -ExecutionPolicy Bypass: defends against per-machine policy hooks that
        // can slow cold-start by 1-2s under Tamper Protection / Application Control.
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(
            "Get-CimInstance Win32_Process -Filter \"Name='msedgewebview2.exe'\" | " +
            "Where-Object { $_.CommandLine -like '*studiopro.exe*' } | " +
            "Select-Object -ExpandProperty CommandLine");

        string output;
        try
        {
            using var p = Process.Start(psi)
                ?? throw new TransportUnavailable("Could not launch powershell.exe for studiopro.exe lookup.");
            // CIM query output is large: each msedgewebview2.exe child (renderer,
            // GPU, utility, etc.) returns its full command line, so total output
            // can easily be 15-30KB. The OS pipe buffer is ~4-8KB on Windows —
            // if we don't drain stdout concurrently with WaitForExit, PowerShell
            // blocks writing to a full pipe, never exits, and our WaitForExit
            // times out. This was the actual failure mode under live Studio Pro:
            // direct invocation completed in 0.7s, in-process timed out at 15s.
            var outputTask = Task.Run(() => p.StandardOutput.ReadToEnd());
            const int timeoutMs = 10000;
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                throw new TransportUnavailable($"studiopro.exe lookup timed out ({timeoutMs / 1000}s)");
            }
            output = outputTask.Result;
        }
        catch (Exception ex) when (ex is not TransportUnavailable)
        {
            throw new TransportUnavailable($"PowerShell CIM query failed: {ex.Message}", ex);
        }

        var ports = new HashSet<int>();
        foreach (Match m in Regex.Matches(output, @"--remote-debugging-port=(\d+)"))
        {
            if (int.TryParse(m.Groups[1].Value, out var port)) ports.Add(port);
        }

        if (ports.Count == 0)
            throw new TransportUnavailable(
                "Studio Pro WebView2 has no --remote-debugging-port (Studio Pro not running, or running with debug port disabled).");
        if (ports.Count > 1)
            throw new TransportUnavailable(
                $"Multiple Studio Pro instances detected (ports {string.Join(',', ports.OrderBy(x => x))}). " +
                "Close all but one Studio Pro instance and retry.");
        return ports.First();
    }

    private static async Task<string> DiscoverMaiaTargetAsync(int port, CancellationToken ct)
    {
        string json;
        try
        {
            json = await Http.GetStringAsync($"http://127.0.0.1:{port}/json", ct);
        }
        catch (Exception ex)
        {
            throw new TransportUnavailable($"CDP endpoint :{port}/json unreachable: {ex.Message}", ex);
        }

        var root = JsonNode.Parse(json) as JsonArray
            ?? throw new TransportUnavailable($"CDP /json on :{port} returned non-array");

        foreach (var t in root)
        {
            var url = t?["url"]?.GetValue<string>() ?? "";
            if (url.Contains(TargetUrlSubstr, StringComparison.OrdinalIgnoreCase))
            {
                var wsUrl = t?["webSocketDebuggerUrl"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(wsUrl)) return wsUrl;
            }
        }
        throw new TransportUnavailable(
            "Maia panel not visible. In Studio Pro click the Maia tab (right pane) and retry.");
    }

    public async Task<JsonNode?> EvaluateAsync(string js, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        try
        {
            return await EvaluateOnceAsync(js, timeout, ct);
        }
        catch (TransportUnavailable ex) when (ex.IsDisconnect)
        {
            // One-shot reconnect on transport-level drop. This is the v4.2.0
            // anti-storm path: instead of every caller seeing IOException +
            // bridge-dead, the next eval transparently reconnects + retries.
            log?.Debug($"[cdp] socket drop detected; reconnecting once: {ex.Message}");
            await connectGate.WaitAsync(ct);
            try { await ConnectInternalAsync(ct); }
            finally { connectGate.Release(); }
            return await EvaluateOnceAsync(js, timeout, ct);
        }
    }

    private async Task<JsonNode?> EvaluateOnceAsync(string js, TimeSpan? timeout, CancellationToken ct)
    {
        if (ws is null || ws.State != WebSocketState.Open)
            throw new TransportUnavailable("CDP client is not connected.") { IsDisconnect = true };

        await evalGate.WaitAsync(ct);
        try
        {
            int id = Interlocked.Increment(ref messageId);
            var req = new JsonObject
            {
                ["id"] = id,
                ["method"] = "Runtime.evaluate",
                ["params"] = new JsonObject
                {
                    ["expression"] = $"(() => {{ {js} }})()",
                    ["returnByValue"] = true,
                    ["awaitPromise"] = true,
                }
            };
            var requestText = req.ToJsonString();
            log?.Debug($"[cdp] >> id={id} bytes={requestText.Length}");
            var bytes = Encoding.UTF8.GetBytes(requestText);

            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            sendCts.CancelAfter(timeout ?? EvaluateDefaultTimeout);
            try
            {
                await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, sendCts.Token);
            }
            catch (WebSocketException wex)
            {
                try { ws.Dispose(); } catch { }
                ws = null;
                throw new TransportUnavailable($"WebSocket send failed: {wex.Message}", wex) { IsDisconnect = true };
            }
            catch (System.IO.IOException ioex)
            {
                try { ws.Dispose(); } catch { }
                ws = null;
                throw new TransportUnavailable($"WebSocket send failed: {ioex.Message}", ioex) { IsDisconnect = true };
            }

            // Read until we see our id.
            var deadline = DateTime.UtcNow + (timeout ?? EvaluateDefaultTimeout);
            var buf = new ArraySegment<byte>(new byte[64 * 1024]);
            var sb = new StringBuilder();
            while (DateTime.UtcNow < deadline)
            {
                sb.Clear();
                while (true)
                {
                    using var recvCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        throw new TransportUnavailable($"CDP Runtime.evaluate timed out after {(timeout ?? EvaluateDefaultTimeout).TotalSeconds:0.0}s");
                    recvCts.CancelAfter(remaining);
                    WebSocketReceiveResult r;
                    try { r = await ws.ReceiveAsync(buf, recvCts.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TransportUnavailable($"CDP Runtime.evaluate timed out after {(timeout ?? EvaluateDefaultTimeout).TotalSeconds:0.0}s");
                    }
                    catch (WebSocketException wex)
                    {
                        try { ws.Dispose(); } catch { }
                        ws = null;
                        throw new TransportUnavailable($"WebSocket receive failed: {wex.Message}", wex) { IsDisconnect = true };
                    }
                    catch (System.IO.IOException ioex)
                    {
                        try { ws.Dispose(); } catch { }
                        ws = null;
                        throw new TransportUnavailable($"WebSocket receive failed: {ioex.Message}", ioex) { IsDisconnect = true };
                    }
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        try { ws.Dispose(); } catch { }
                        ws = null;
                        throw new TransportUnavailable("WebSocket closed by remote.") { IsDisconnect = true };
                    }
                    sb.Append(Encoding.UTF8.GetString(buf.Array!, 0, r.Count));
                    if (r.EndOfMessage) break;
                }
                var msg = JsonNode.Parse(sb.ToString()) as JsonObject;
                if (msg is null) continue;
                if (msg["id"] is JsonValue v && v.TryGetValue<int>(out var msgId) && msgId == id)
                {
                    log?.Debug($"[cdp] << id={id} bytes={sb.Length}");
                    if (msg["error"] is JsonObject err)
                        throw new CdpProtocolException("Runtime.evaluate", msg, $"CDP error: {err["message"]?.GetValue<string>()}");
                    var result = msg["result"]?["result"];
                    if (result?["exceptionDetails"] is not null)
                        throw new TransportUnavailable($"JS exception inside Maia WebView: {result["exceptionDetails"]?.ToJsonString()}");
                    return result?["value"];
                }
                // Not our id — ignore (events / other responses) and keep reading.
            }
            throw new TransportUnavailable($"CDP Runtime.evaluate timed out after {(timeout ?? EvaluateDefaultTimeout).TotalSeconds:0.0}s");
        }
        finally { evalGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (ws is not null)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch { /* best-effort */ }
            try { ws.Dispose(); } catch { }
            ws = null;
        }
        try { evalGate.Dispose(); } catch { }
        try { connectGate.Dispose(); } catch { }
    }
}
