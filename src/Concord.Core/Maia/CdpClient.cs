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

    private IWebSocketAdapter? ws;
    private int messageId;
    private readonly SemaphoreSlim evalGate = new(1, 1);
    private readonly SemaphoreSlim connectGate = new(1, 1);

    // v4.2.1: socket factory + discovery override let unit tests inject
    // controllable fakes for reconnect-on-drop coverage. Production uses
    // the default factory (() => new ClientWebSocketAdapter()) and the
    // built-in WMI/HTTP discovery; tests override either or both.
    private readonly Func<IWebSocketAdapter> webSocketFactory;
    private readonly Func<CancellationToken, Task<(int port, string targetWsUrl)>>? discoveryOverride;

    private int? cachedPort;
    private string? cachedTargetWsUrl;
    private DateTime cachedDiscoveryAt;

    private readonly Logger? log;

    // Heartbeat: every HeartbeatIntervalSec, fire a no-op `1+1` Runtime.evaluate
    // over the persistent socket. Defeats Chromium's WebView2 background-tab
    // throttling that otherwise drifts the maia_agent.js setInterval timer
    // from 500ms to seconds when Maia is hidden behind another pane.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    // Bounded heartbeat eval timeout — must be SHORTER than the eval-gate
    // wait we'd otherwise hang on if the gate is held by a long maia__wait.
    // 3s is long enough for a healthy round-trip and short enough that a
    // gate-held skip is observable in DEBUG logs without burning a full beat.
    private static readonly TimeSpan HeartbeatEvalTimeout = TimeSpan.FromSeconds(3);
    // Safety cap: if N consecutive beats time out (gate held forever, or
    // socket wedged), treat as a bridge problem and stop the loop. The next
    // real Evaluate triggers reconnect via the EvaluateAsync wrapper, which
    // also restarts the heartbeat via ConnectInternalAsync.
    private const int HeartbeatMaxConsecutiveSkips = 12;   // 2 minutes of skips
    private CancellationTokenSource? heartbeatCts;
    private Task? heartbeatTask;

    public CdpClient() : this(null) { }
    public CdpClient(Logger? log)
        : this(log, webSocketFactory: null, discoveryOverride: null) { }

    /// <summary>
    /// Test-friendly constructor. <paramref name="webSocketFactory"/>: returns
    /// a fresh adapter per (re)connect; defaults to a real ClientWebSocket.
    /// <paramref name="discoveryOverride"/>: returns the (port, ws-url) pair
    /// without running the WMI / HTTP scan; null falls through to the
    /// production discovery path.
    /// </summary>
    internal CdpClient(
        Logger? log,
        Func<IWebSocketAdapter>? webSocketFactory,
        Func<CancellationToken, Task<(int port, string targetWsUrl)>>? discoveryOverride)
    {
        this.log = log;
        this.webSocketFactory = webSocketFactory ?? (() => new ClientWebSocketAdapter());
        this.discoveryOverride = discoveryOverride;
    }

    public async Task ConnectMaiaAsync(CancellationToken ct)
    {
        // The Windows-only guard applies to production discovery; tests using
        // discoveryOverride can run on any platform.
        if (discoveryOverride is null && !OperatingSystem.IsWindows())
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
        else if (discoveryOverride is not null)
        {
            // v4.2.1 test seam — bypass WMI/HTTP scan entirely.
            (port, targetWsUrl) = await discoveryOverride(ct);
            cachedPort = port;
            cachedTargetWsUrl = targetWsUrl;
            cachedDiscoveryAt = DateTime.UtcNow;
            log?.Debug($"[cdp] discovery override -> port={port}");
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

        ws = webSocketFactory();
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

        // Heartbeat lifecycle: cancel the previous loop and AWAIT it (with a
        // bounded timeout) before swapping in the new one. Without the await,
        // a reconnect mid-flight could leave the old loop in the middle of an
        // Evaluate, racing the new loop on the same gate / correlation IDs.
        await StartHeartbeatAsync();
    }

    private async Task StartHeartbeatAsync()
    {
        // Cancel the previous loop and let it drain.
        if (heartbeatTask is { } old)
        {
            try { heartbeatCts?.Cancel(); } catch { }
            try { await old.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch { /* best-effort drain */ }
            try { heartbeatCts?.Dispose(); } catch { }
        }
        heartbeatCts = new CancellationTokenSource();
        var token = heartbeatCts.Token;
        heartbeatTask = Task.Run(() => HeartbeatLoopAsync(token));
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        int consecutiveSkips = 0;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(HeartbeatInterval, ct); }
            catch (OperationCanceledException) { return; }

            if (ws is null || ws.State != WebSocketState.Open) return;
            try
            {
                // v4.2.1: heartbeat also triggers __maiaBridge.scan() when the
                // bridge is installed. Defeats Chromium's WebView2 background-
                // tab throttling that otherwise drifts the JS-side
                // setInterval(scanForCompletions, 500) from 500ms toward
                // seconds when Maia is hidden behind another pane. The
                // persistent CDP socket is unaffected by tab visibility, so
                // pulling the scan from C#'s heartbeat keeps detection
                // latency bounded at HeartbeatInterval (10s) regardless of
                // pane visibility. Cheap when no tickets are pending; the
                // scan is O(n) on the ticket map (capped at 100 entries).
                await EvaluateOnceAsync(
                    "if (window.__maiaBridge && window.__maiaBridge.scan) { window.__maiaBridge.scan(); } return 1+1;",
                    HeartbeatEvalTimeout, ct);
                consecutiveSkips = 0;
                log?.Debug("[cdp] heartbeat ok (scan triggered if bridge present)");
            }
            catch (TransportUnavailable ex) when (!ex.IsDisconnect && ex.Message.Contains("timed out"))
            {
                // Gate held by a long Evaluate — DON'T treat as a real failure.
                // Just skip this beat. Reviewer B2: returning here would kill
                // the heartbeat for the rest of this connection's life.
                consecutiveSkips++;
                log?.Debug($"[cdp] heartbeat skipped (gate held), {consecutiveSkips}/{HeartbeatMaxConsecutiveSkips}");
                if (consecutiveSkips >= HeartbeatMaxConsecutiveSkips)
                {
                    log?.Debug("[cdp] heartbeat skip cap reached — stopping loop; next eval will reconnect");
                    return;
                }
            }
            catch (TransportUnavailable ex)
            {
                // Real disconnect — let the loop die. The next user-driven
                // Evaluate will trigger reconnect via the EvaluateAsync wrapper,
                // which calls StartHeartbeatAsync inside ConnectInternalAsync.
                log?.Debug($"[cdp] heartbeat failed: {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                log?.Debug($"[cdp] heartbeat unexpected: {ex.GetType().Name}: {ex.Message}");
                return;
            }
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
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, sendCts.Token);
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
        // Stop the heartbeat first so it doesn't try to grab the gate during
        // teardown. Every step is wrapped — DisposeAsync must be safe under
        // double-call (e.g. settings toggle off → on, or shutdown after a
        // failed start).
        try { heartbeatCts?.Cancel(); } catch { }
        if (heartbeatTask is { } ht)
        {
            try { await ht.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
        try { heartbeatCts?.Dispose(); } catch { }
        heartbeatCts = null;
        heartbeatTask = null;

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
