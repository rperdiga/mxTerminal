using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Terminal;

/// <summary>
/// In-process MCP streamable-HTTP server. Listens on 127.0.0.1:port.
/// Implements three JSON-RPC methods: initialize, tools/list, tools/call.
/// One-action-at-a-time serialization is enforced inside <see cref="StudioProActions"/>.
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
    private HttpListener? listener;
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

        // Try the requested port first; if it's taken (HttpListener throws on
        // bind), fall back to a free OS-picked port. The user no longer sees
        // a port input — we surface whatever we end up bound to via the
        // settings payload + status pill.
        boundPort = requestedPort > 0 ? requestedPort : PickFreePort();
        listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{boundPort}/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex) when (requestedPort > 0)
        {
            log?.Warn($"[actions] requested port {requestedPort} unavailable ({ex.Message}); falling back to a free port");
            try { listener.Close(); } catch { }
            boundPort = PickFreePort();
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{boundPort}/");
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
        try { listener?.Close(); } catch { }
        try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        listener = null;
        cts = null;
        loop = null;
    }

    private static int PickFreePort()
    {
        // HttpListener doesn't support port 0; bind a TcpListener to discover one.
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener!.GetContextAsync(); }
            catch (HttpListenerException) { return; }   // Stop() called
            catch (ObjectDisposedException) { return; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            if (ctx.Request.Url?.AbsolutePath != "/mcp" || ctx.Request.HttpMethod != "POST")
            {
                await Respond(ctx, 404, """{"error":"not found"}""");
                return;
            }

            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();

            JsonNode? root;
            try { root = JsonNode.Parse(body); }
            catch
            {
                await RespondJson(ctx, BuildError(id: null, code: -32700, message: "Parse error"));
                return;
            }
            if (root is not JsonObject req)
            {
                await RespondJson(ctx, BuildError(id: null, code: -32600, message: "Invalid Request"));
                return;
            }

            var id = req["id"];
            var method = req["method"]?.GetValue<string>();
            var pars = req["params"] as JsonObject;

            JsonNode result = method switch
            {
                "initialize" => HandleInitialize(),
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleToolsCallAsync(pars),
                _ => BuildErrorBody(code: -32601, message: $"Method '{method}' not found"),
            };

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
            await RespondJson(ctx, envelope);
        }
        catch (Exception ex)
        {
            log?.Error("[actions] request failure", ex);
            try { await RespondJson(ctx, BuildError(id: null, code: -32603, message: $"Internal error: {ex.Message}")); }
            catch { }
        }
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
                "Save all unsaved changes in Studio Pro (Ctrl+S). Use before triggering a run or external CLI op so disk and the in-memory model agree."),
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

    private static Task RespondJson(HttpListenerContext ctx, JsonNode body) =>
        Respond(ctx, 200, body.ToJsonString());

    private static async Task Respond(HttpListenerContext ctx, int status, string body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.OutputStream.Close();
    }
}
