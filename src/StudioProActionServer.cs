using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Terminal;

/// <summary>
/// In-process MCP streamable-HTTP server. Listens on 127.0.0.1:port.
/// Implements three JSON-RPC methods: initialize, tools/list, tools/call.
/// One-action-at-a-time serialization is enforced inside <see cref="StudioProActions"/>.
/// <para>
/// Why hand-rolled HTTP/1.1 over TcpListener instead of HttpListener:
/// .NET's HttpListener on macOS does NOT properly isolate prefixes by port —
/// requests to one prefix can be answered by an unrelated HttpListener
/// instance in the same process (verified 2026-05-07: probes to localhost:55169
/// were being answered by Studio Pro's HttpListener on 7782 with a "Microsoft-NetCore/2.0"
/// 404). HttpListener is also not officially supported on macOS by .NET.
/// TcpListener is cross-platform, well-supported, and our HTTP needs are
/// tiny (POST /mcp, JSON in/out, single response).
/// </para>
/// </summary>
public sealed class StudioProActionServer : IDisposable
{
    public const string ServerName = "mendix-studio-pro-actions";
    public const string ServerVersion = "1.0.0";
    /// <summary>
    /// Fixed default port for the Action bridge. The user has no setting for
    /// this — the bridge auto-binds 7783 (chosen because it's the original
    /// default Studio Pro itself uses for ITS MCP, so the convention is
    /// "Mendix things live in the 778x range") and falls back to a free port
    /// at startup if 7783 is busy. Saved values in older settings.json
    /// files are ignored.
    /// </summary>
    public const int DefaultPort = 7783;

    private readonly StudioProActions actions;
    private readonly Logger? log;
    private TcpListener? listener;
    private int boundPort;
    private CancellationTokenSource? cts;
    private Task? loop;
    private readonly int requestedPort;

    public StudioProActionServer(StudioProActions actions, int port, Logger? log = null)
    {
        this.actions = actions;
        this.log = log;
        this.requestedPort = port;
    }

    /// <summary>Bound port. Valid only after <see cref="Start"/> succeeds.</summary>
    public int Port => boundPort;

    public void Start()
    {
        if (listener != null) throw new InvalidOperationException("Server already started");

        // Try the requested port first; if it's taken (TcpListener.Start throws
        // SocketException with AddressAlreadyInUse), fall back to an OS-picked
        // free port. The user no longer sees a port input — we surface whatever
        // we end up bound to via the settings payload + status pill.
        boundPort = requestedPort > 0 ? requestedPort : PickFreePort();
        listener = new TcpListener(IPAddress.Loopback, boundPort);
        try
        {
            listener.Start();
        }
        catch (SocketException ex) when (requestedPort > 0 && ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            log?.Warn($"[actions] requested port {requestedPort} unavailable ({ex.Message}); falling back to a free port");
            try { listener.Stop(); } catch { }
            boundPort = PickFreePort();
            listener = new TcpListener(IPAddress.Loopback, boundPort);
            listener.Start();
        }
        cts = new CancellationTokenSource();
        loop = Task.Run(() => AcceptLoopAsync(cts.Token));
        log?.Info($"[actions] HTTP server listening on http://127.0.0.1:{boundPort}/mcp");
    }

    public void Dispose()
    {
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        listener = null;
        cts = null;
        loop = null;
    }

    private static int PickFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener!.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }   // Stop() called
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted ||
                                             ex.SocketErrorCode == SocketError.Interrupted)
            {
                return;
            }
            catch (Exception ex)
            {
                log?.Error($"[actions] accept-loop killed by {ex.GetType().FullName}", ex);
                return;
            }
            _ = Task.Run(() => HandleConnectionAsync(client));
        }
    }

    private async Task HandleConnectionAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                stream.ReadTimeout = 5000;
                stream.WriteTimeout = 5000;
                var (method, path, headers, body) = await ReadHttpRequestAsync(stream);

                if (method == null)
                {
                    // Malformed request — close the connection silently.
                    return;
                }

                if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) || path != "/mcp")
                {
                    await WriteResponseAsync(stream, 404, """{"error":"not found"}""");
                    return;
                }

                var (status, responseBody) = await BuildJsonRpcResponseAsync(body);
                await WriteResponseAsync(stream, status, responseBody);
            }
        }
        catch (Exception ex)
        {
            log?.Warn($"[actions] connection failure: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Read an HTTP/1.1 request: request-line, headers, then exactly Content-Length
    /// bytes of body (or empty if header absent). Minimal — we only support what
    /// MCP clients actually send: a POST with Content-Length and a JSON body.
    /// Returns (null, null, _, _) if the request is malformed.
    /// </summary>
    private static async Task<(string? method, string? path, Dictionary<string, string> headers, string body)>
        ReadHttpRequestAsync(NetworkStream stream)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requestLine = await ReadLineAsync(stream);
        if (string.IsNullOrEmpty(requestLine)) return (null, null, headers, "");

        var parts = requestLine.Split(' ');
        if (parts.Length < 2) return (null, null, headers, "");
        var method = parts[0];
        var path = parts[1];
        var qIdx = path.IndexOf('?');
        if (qIdx >= 0) path = path.Substring(0, qIdx);

        while (true)
        {
            var line = await ReadLineAsync(stream);
            if (string.IsNullOrEmpty(line)) break;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            headers[name] = value;
        }

        var body = "";
        if (headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var len) && len > 0)
        {
            // Cap the body at 1 MB — JSON-RPC envelopes for our six tools
            // are tiny; a request bigger than this is almost certainly malicious.
            const int maxBody = 1 * 1024 * 1024;
            if (len > maxBody) throw new InvalidOperationException($"body too large: {len}");
            var buf = new byte[len];
            int read = 0;
            while (read < len)
            {
                var n = await stream.ReadAsync(buf.AsMemory(read, len - read));
                if (n == 0) break;
                read += n;
            }
            body = Encoding.UTF8.GetString(buf, 0, read);
        }
        return (method, path, headers, body);
    }

    /// <summary>
    /// Read one CRLF- or LF-terminated line from the stream, returning the
    /// content without the terminator. Returns empty string for an empty line
    /// (header/body separator) and throws on EOF before a line is complete.
    /// </summary>
    private static async Task<string> ReadLineAsync(NetworkStream stream)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(buf.AsMemory(0, 1));
            if (n == 0)
            {
                if (sb.Length == 0) return "";
                throw new EndOfStreamException("unexpected EOF mid-line");
            }
            var b = buf[0];
            if (b == (byte)'\n')
            {
                // Strip a trailing CR if present.
                if (sb.Length > 0 && sb[sb.Length - 1] == '\r') sb.Length--;
                return sb.ToString();
            }
            sb.Append((char)b);
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string body)
    {
        var reason = status switch
        {
            200 => "OK",
            202 => "Accepted",
            400 => "Bad Request",
            404 => "Not Found",
            500 => "Internal Server Error",
            _   => "Status",
        };
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var head =
            $"HTTP/1.1 {status} {reason}\r\n" +
            $"Content-Type: application/json\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            $"Connection: close\r\n\r\n";
        var headBytes = Encoding.UTF8.GetBytes(head);
        await stream.WriteAsync(headBytes);
        await stream.WriteAsync(bodyBytes);
        await stream.FlushAsync();
    }

    /// <summary>
    /// Process a JSON-RPC body and produce (HTTP status, response body). For
    /// notifications (no <c>id</c> in the request) we MUST NOT send a JSON-RPC
    /// response — per the JSON-RPC 2.0 spec — and per MCP streamable-HTTP we
    /// reply with HTTP 202 Accepted and an empty body. Returning a JSON-RPC
    /// error response to a notification breaks the Codex client (verified
    /// 2026-05-07: Codex sends <c>notifications/initialized</c> after a
    /// successful initialize handshake, sees our error response with
    /// <c>id: null</c>, and treats the transport as closed).
    /// </summary>
    private async Task<(int status, string body)> BuildJsonRpcResponseAsync(string body)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(body); }
        catch
        {
            return (200, BuildError(id: null, code: -32700, message: "Parse error").ToJsonString());
        }
        if (root is not JsonObject req)
        {
            return (200, BuildError(id: null, code: -32600, message: "Invalid Request").ToJsonString());
        }

        var hasId = req.ContainsKey("id") && req["id"] is not null;
        var id = req["id"];
        var method = req["method"]?.GetValue<string>();
        var pars = req["params"] as JsonObject;

        // Notifications: acknowledge with 202 + empty body. We don't
        // currently act on any client→server notification (e.g.
        // notifications/initialized, notifications/cancelled), but
        // accepting them is required for the transport handshake.
        if (!hasId)
            return (202, "");

        JsonNode result;
        try
        {
            result = method switch
            {
                "initialize" => HandleInitialize(),
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleToolsCallAsync(pars),
                _ => BuildErrorBody(code: -32601, message: $"Method '{method}' not found"),
            };
        }
        catch (Exception ex)
        {
            log?.Error("[actions] request failure", ex);
            return (200, BuildError(id: id, code: -32603, message: $"Internal error: {ex.Message}").ToJsonString());
        }

        JsonObject envelope;
        if (result is JsonObject obj && obj.ContainsKey("__error"))
        {
            var err = (JsonObject)obj["__error"]!.DeepClone();
            envelope = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = err };
        }
        else
        {
            envelope = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };
        }
        return (200, envelope.ToJsonString());
    }

    private static JsonNode HandleInitialize() => new JsonObject
    {
        ["protocolVersion"] = "2025-03-26",
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
        ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = ServerVersion },
    };

    private static JsonNode HandleToolsList() => new JsonObject
    {
        ["tools"] = new JsonArray
        {
            ToolDef("run_app",
                "Start the local Mendix runtime for the currently open Studio Pro app. If already running, returns 'already_running' without disturbing it."),
            ToolDef("stop_app",
                "Stop the local Mendix runtime. No-op if it isn't running."),
            ToolDef("refresh_project",
                "Reload the project model from disk. Use after editing model files (e.g. microflow XML) outside Studio Pro to make the IDE pick up the changes."),
            ToolDef("save_all",
                "Best-effort save: posts Ctrl+S to Studio Pro's main window. Works when the active document tab has focus; if the user's focus is elsewhere (e.g. inside this terminal), Studio Pro routes the keystroke to the focused child instead and the save may not fire. For guaranteed save, ask the user to click the document tab once first."),
            ToolDef("get_active_run_configuration",
                "Read-only: returns the currently selected local run configuration (id, name, applicationRootUrl). Useful for confirming which environment a run/stop will affect."),
            ToolDef("get_app_status",
                "Composite read-only snapshot for orienting: project path/name, run state (running|stopped|unknown), running URL if any, active run configuration. Call this first when starting work in a fresh Claude Code session."),
        }
    };

    private static JsonObject ToolDef(string name, string description) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray(),
        }
    };

    private async Task<JsonNode> HandleToolsCallAsync(JsonObject? pars)
    {
        var name = pars?["name"]?.GetValue<string>();
        ActionResult result = name switch
        {
            "run_app"                       => await actions.RunAppAsync(),
            "stop_app"                      => await actions.StopAppAsync(),
            "refresh_project"               => await actions.RefreshProjectAsync(),
            "save_all"                      => await actions.SaveAllAsync(),
            "get_active_run_configuration"  => await actions.GetActiveRunConfigurationAsync(),
            "get_app_status"                => await actions.GetAppStatusAsync(),
            _ => null!,
        };

        if (result is null)
            return BuildErrorBody(code: -32601, message: $"Unknown tool '{name}'");

        // MCP tools/call result format: result.content = [ { type: "text", text: "<json>" } ]
        var payload = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = payload }
            },
            ["isError"] = result.Error != null,
        };
    }

    private static JsonObject BuildError(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static JsonObject BuildErrorBody(int code, string message) =>
        new() { ["__error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
