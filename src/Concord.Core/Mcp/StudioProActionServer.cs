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
    public const string ServerName = "concord-mcp";
    public const string ServerVersion = "1.3.0";
    /// <summary>
    /// Fixed default port for the Action bridge. The user has no setting for
    /// this — the bridge auto-binds 7783 (chosen because it's the original
    /// default Studio Pro itself uses for ITS MCP, so the convention is
    /// "Mendix things live in the 778x range") and falls back to a free port
    /// at startup if 7783 is busy. Saved values in older settings.json
    /// files are ignored.
    /// </summary>
    public const int DefaultPort = 7783;

    private readonly Logger? log;
    private TcpListener? listener;
    private int boundPort;
    private CancellationTokenSource? cts;
    private Task? loop;
    private readonly int requestedPort;

    public StudioProActionServer(int port, Logger? log = null)
    {
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

    private JsonNode HandleToolsList()
    {
        // Build a dedup dictionary keyed case-insensitively.
        // Catalog entries are added first so they shadow any hardcoded entry
        // with the same name (no collisions expected today, but safety).
        var byName = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);

        var activeCatalog = Terminal.Mcp.ToolCatalogRegistry.Active;
        if (activeCatalog != null)
        {
            foreach (var tool in activeCatalog.ListVisibleTools())
            {
                byName[tool.Name] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = $"Concord SPMCP tool ({tool.Family}). Schema TBD.",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = true,
                    },
                };
            }
        }

        var arr = new JsonArray();
        foreach (var entry in byName.Values.OrderBy(t => t["name"]?.GetValue<string>(), StringComparer.OrdinalIgnoreCase))
            arr.Add(entry);
        return new JsonObject { ["tools"] = arr };
    }

    private async Task<JsonNode> HandleToolsCallAsync(JsonObject? pars)
    {
        var name = pars?["name"]?.GetValue<string>();
        var args = pars?["arguments"] as JsonObject ?? new JsonObject();

        // --- Catalog dispatch (single path for all tools) ---
        // All tools (SPMCP, UI-actions, Maia) are registered in the ToolCatalog
        // at MEF activation time. Family toggles via SetFamilyEnabled gate
        // visibility. If a tool isn't in the catalog (or the catalog is null),
        // return the standard JSON-RPC "method not found" error.
        var catalog = Terminal.Mcp.ToolCatalogRegistry.Active;
        if (catalog != null && name != null)
        {
            var visible = catalog.ListVisibleNames();
            if (visible.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var resultObj = await catalog.InvokeAsync(name, args);
                    var payloadJson = resultObj as string ?? System.Text.Json.JsonSerializer.Serialize(
                        resultObj,
                        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                        {
                            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                        });
                    return new JsonObject
                    {
                        ["content"] = new JsonArray
                        {
                            new JsonObject { ["type"] = "text", ["text"] = payloadJson }
                        },
                        ["isError"] = false,
                    };
                }
                catch (Exception ex)
                {
                    log?.Warn($"[concord-mcp] catalog tool '{name}' failed: {ex.Message}");
                    return new JsonObject
                    {
                        ["content"] = new JsonArray
                        {
                            new JsonObject { ["type"] = "text", ["text"] = $"{{\"error\":\"{System.Text.Json.JsonEncodedText.Encode(ex.Message)}\"}}" }
                        },
                        ["isError"] = true,
                    };
                }
            }
        }

        return BuildErrorBody(code: -32601, message: $"Unknown tool '{name}'");
    }

    private static JsonObject BuildError(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static JsonObject BuildErrorBody(int code, string message) =>
        new() { ["__error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
