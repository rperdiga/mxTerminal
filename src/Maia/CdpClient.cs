using System.Management;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Net.Http;

namespace Terminal.Maia;

/// <summary>
/// Cross-platform-aware: on non-Windows, ConnectMaiaAsync immediately raises
/// TransportUnavailable("not supported on this platform") so the router
/// reports zero tiers. The main implementation is Windows + WMI + WebSocket.
/// </summary>
public sealed class CdpClient : ICdpClient
{
    private const string TargetUrlSubstr = "maia-agent";
    private static readonly TimeSpan EvaluateDefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private ClientWebSocket? ws;
    private int messageId;
    private readonly SemaphoreSlim evalGate = new(1, 1);

    public async Task ConnectMaiaAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new TransportUnavailable("Maia bridge is Windows-only in this Concord release.");

        int port = FindDebugPort();
        string targetWsUrl = await DiscoverMaiaTargetAsync(port, ct);

        ws = new ClientWebSocket();
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(5));
            await ws.ConnectAsync(new Uri(targetWsUrl), connectCts.Token);
        }
        catch (Exception ex) when (ex is not TransportUnavailable)
        {
            ws.Dispose();
            ws = null;
            throw new TransportUnavailable($"WebSocket connect to Maia target failed: {ex.Message}", ex);
        }
    }

    private static int FindDebugPort()
    {
        // WMI: enumerate msedgewebview2.exe whose CommandLine references studiopro.exe.
        // If multiple distinct ports are found, fail loud — driving the wrong project
        // silently is the worst possible outcome.
        var ports = new HashSet<int>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE Name = 'msedgewebview2.exe'");
            foreach (ManagementObject mo in searcher.Get())
            {
                var cmdline = (mo["CommandLine"] as string) ?? "";
                if (!cmdline.Contains("studiopro.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var m = Regex.Match(cmdline, @"--remote-debugging-port=(\d+)");
                if (m.Success) ports.Add(int.Parse(m.Groups[1].Value));
            }
        }
        catch (ManagementException ex)
        {
            throw new TransportUnavailable($"WMI process query failed: {ex.Message}", ex);
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
        if (ws is null || ws.State != WebSocketState.Open)
            throw new TransportUnavailable("CDP client is not connected.");

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
            var bytes = Encoding.UTF8.GetBytes(req.ToJsonString());

            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            sendCts.CancelAfter(timeout ?? EvaluateDefaultTimeout);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, sendCts.Token);

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
                    sb.Append(Encoding.UTF8.GetString(buf.Array!, 0, r.Count));
                    if (r.EndOfMessage) break;
                }
                var msg = JsonNode.Parse(sb.ToString()) as JsonObject;
                if (msg is null) continue;
                if (msg["id"] is JsonValue v && v.TryGetValue<int>(out var msgId) && msgId == id)
                {
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
            ws.Dispose();
            ws = null;
        }
        evalGate.Dispose();
    }
}
