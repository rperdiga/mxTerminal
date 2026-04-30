using Eto.Forms;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;
using Terminal.Messages;
using System.Text.Json;

namespace Terminal;

public sealed class TerminalPaneViewModel : WebViewDockablePaneViewModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TerminalSessionManager manager;
    private readonly Func<IModel?> getCurrentApp;
    private readonly Uri webIndexUri;
    private readonly Logger log;

    private IWebView? webView;
    private Action<string, byte[]>? outputHandler;
    private Action<string, int?>?  exitedHandler;

    public TerminalPaneViewModel(
        string title,
        TerminalSessionManager manager,
        Func<IModel?> getCurrentApp,
        Uri webIndexUri,
        Logger log)
    {
        Title = title;
        this.manager = manager;
        this.getCurrentApp = getCurrentApp;
        this.webIndexUri = webIndexUri;
        this.log = log;
    }

    public override void InitWebView(IWebView webView)
    {
        this.webView = webView;
        webView.MessageReceived += OnWebViewMessage;
        webView.Address = webIndexUri;
        // Allow right-click → Inspect inside the WebView for diagnostics.
        try { ((dynamic)webView).AllowedDevTools = true; } catch { /* best-effort */ }
        try { ((dynamic)webView).AllowReload    = true; } catch { /* best-effort */ }

        outputHandler = (tabId, bytes) => Post("output", new OutputPayload(tabId, Convert.ToBase64String(bytes)));
        exitedHandler = (tabId, code) => Post("exit", new ExitPayload(tabId, code));
        manager.Output += outputHandler;
        manager.Exited += exitedHandler;

        OnClosed += () =>
        {
            if (outputHandler != null) manager.Output -= outputHandler;
            if (exitedHandler != null) manager.Exited -= exitedHandler;
            outputHandler = null;
            exitedHandler = null;
        };
    }

    private void OnWebViewMessage(object? sender, MessageReceivedEventArgs e)
    {
        try
        {
            switch (e.Message)
            {
                case "ready":
                case "listTabs":
                    Post("tabsList", new TabsListPayload(
                        manager.ListSessions().Select(s => new SessionInfoPayload(s.TabId, s.Title, s.ShellPath, s.Cwd, s.Alive)).ToList()
                    ));
                    break;

                case "createTab":
                    HandleCreateTab(GetData<CreateTabPayload>(e));
                    break;

                case "closeTab":
                {
                    var p = GetData<CloseTabPayload>(e);
                    manager.Close(p.TabId);
                    Post("tabClosed", new TabClosedPayload(p.TabId));
                    break;
                }

                case "input":
                {
                    var p = GetData<InputPayload>(e);
                    var bytes = Convert.FromBase64String(p.DataB64);
                    // DIAGNOSTIC — remove once paste duplication / truncation is resolved.
                    var preview = bytes.Length <= 64
                        ? Convert.ToHexString(bytes)
                        : Convert.ToHexString(bytes.AsSpan(0, 32).ToArray()) + "..." + Convert.ToHexString(bytes.AsSpan(bytes.Length - 8).ToArray());
                    log.Info($"input tab={p.TabId.Substring(0, 8)} len={bytes.Length} preview={preview}");
                    manager.Write(p.TabId, bytes);
                    break;
                }

                case "resize":
                {
                    var p = GetData<ResizePayload>(e);
                    manager.Resize(p.TabId, p.Cols, p.Rows);
                    break;
                }

                case "replay":
                {
                    var p = GetData<ReplayPayload>(e);
                    var snap = manager.SnapshotBuffer(p.TabId);
                    Post("replayData", new ReplayDataPayload(p.TabId, Convert.ToBase64String(snap)));
                    break;
                }

                case "openSettings":
                {
                    var dir = GetProjectDir();
                    var s = dir != null ? TerminalSettings.Load(dir) : TerminalSettings.Defaults();
                    Post("settings", BuildSettingsPayload(s));
                    break;
                }

                case "saveSettings":
                {
                    HandleSaveSettings(GetData<SaveSettingsPayload>(e));
                    break;
                }

                case "showDevTools":
                {
                    try { webView?.ShowDevTools(); }
                    catch (Exception ex) { log.Warn($"ShowDevTools failed: {ex.Message}"); }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"OnWebViewMessage({e.Message}) failed", ex);
            Post("error", new ErrorPayload(ex.Message, e.Message));
        }
    }

    private async void HandleSaveSettings(SaveSettingsPayload p)
    {
        try
        {
            var dir = GetProjectDir();
            if (dir == null) { Post("error", new ErrorPayload("No Mendix app is open")); return; }

            var current = TerminalSettings.Load(dir);
            var newClients = p.McpClients ?? current.McpClients;
            var newEnabled = p.McpEnabled ?? current.McpEnabled;
            var newPort    = p.McpPort    ?? current.McpPort;

            // If MCP is being enabled, probe it first. Refuse to save broken config.
            if (newEnabled)
            {
                var probe = await McpProbe.ProbeAsync(newPort, log);
                if (!probe.Ok)
                {
                    Post("mcpResult", new McpResultPayload(false,
                        $"{probe.Message}. Enable Studio Pro's MCP server in Preferences → Maia → MCP Server, then try again.",
                        Array.Empty<string>()));
                    // Don't persist — leave settings (and toggle) in their previous state.
                    Post("settings", BuildSettingsPayload(current));
                    return;
                }
            }

            var updated = current with
            {
                ShellPath = p.ShellPath,
                Args = p.Args,
                RingBufferKB = p.RingBufferKB ?? current.RingBufferKB,
                XtermScrollbackLines = p.XtermScrollbackLines ?? current.XtermScrollbackLines,
                Theme = p.Theme ?? current.Theme,
                McpEnabled = newEnabled,
                McpPort = newPort,
                McpClients = newClients,
            };

            // Apply MCP file changes BEFORE saving settings, so a write failure
            // here doesn't leave settings claiming success.
            var touched = ApplyMcpConfig(dir, current, updated);

            updated.Save(dir);
            Post("settings", BuildSettingsPayload(updated));

            if (touched.Length > 0)
            {
                Post("mcpResult", new McpResultPayload(true,
                    newEnabled
                        ? $"MCP enabled for {string.Join(", ", touched)}. Restarting open terminals…"
                        : $"MCP disabled (cleaned up: {string.Join(", ", touched)}). Restarting open terminals…",
                    touched));

                // Recycle all open terminals so any running CLI gets killed
                // and a fresh shell can pick up the new config files.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var recycled = await manager.RecycleAllAsync();
                        foreach (var r in recycled)
                        {
                            Post("tabClosed", new TabClosedPayload(r.OldTabId));
                            Post("tabCreated", new TabCreatedPayload(r.NewTabId, r.Title, r.ShellPath, r.Cwd));
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error("RecycleAll after MCP save failed", ex);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            log.Error("SaveSettings failed", ex);
            Post("error", new ErrorPayload($"Save failed: {ex.Message}", "saveSettings"));
        }
    }

    /// <summary>
    /// Apply the diff between previous and new MCP settings to the relevant
    /// CLI config files. Returns human-readable list of CLIs that were touched
    /// (for the result banner).
    /// </summary>
    private string[] ApplyMcpConfig(string projectDir, TerminalSettings prev, TerminalSettings next)
    {
        var prevClients = new HashSet<string>(prev.McpClients, StringComparer.OrdinalIgnoreCase);
        var nextClients = next.McpEnabled
            ? new HashSet<string>(next.McpClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // .mcp.json is shared by Claude Code and Copilot CLI.
        var jsonNeeded = nextClients.Contains("claude") || nextClients.Contains("copilot");
        var jsonHadIt  = prev.McpEnabled && (prevClients.Contains("claude") || prevClients.Contains("copilot"));

        // ~/.codex/config.toml is just for Codex.
        var tomlNeeded = nextClients.Contains("codex");
        var tomlHadIt  = prev.McpEnabled && prevClients.Contains("codex");

        var url = $"http://localhost:{next.McpPort}/mcp";
        var json = new McpJsonConfigurator(projectDir);
        var toml = new McpTomlConfigurator();
        var touched = new List<string>();

        if (jsonNeeded) { json.Upsert(url); touched.Add(LabelForJson(nextClients)); }
        else if (jsonHadIt) { json.Remove(); touched.Add(LabelForJson(prevClients) + " (removed)"); }

        if (tomlNeeded) { toml.Upsert(url); touched.Add("Codex"); }
        else if (tomlHadIt) { toml.Remove(); touched.Add("Codex (removed)"); }

        return touched.ToArray();
    }

    private static string LabelForJson(HashSet<string> clients)
    {
        var parts = new List<string>();
        if (clients.Contains("claude"))  parts.Add("Claude Code");
        if (clients.Contains("copilot")) parts.Add("Copilot CLI");
        return string.Join(" + ", parts);
    }

    private async void HandleCreateTab(CreateTabPayload p)
    {
        try
        {
            var dir = GetProjectDir() ?? Environment.CurrentDirectory;
            var settings = TerminalSettings.Load(GetProjectDir() ?? "");
            var shell = p.ShellPath ?? settings.ShellPath;
            var args = p.Args ?? settings.Args;
            var cwd = p.Cwd ?? dir;

            var tabId = await manager.CreateSessionAsync(shell, args, cwd, p.Cols, p.Rows);
            Post("tabCreated", new TabCreatedPayload(tabId, Path.GetFileNameWithoutExtension(shell), shell, cwd));
        }
        catch (Exception ex)
        {
            log.Error("CreateTab failed", ex);
            Application.Instance.Invoke(() => Post("error", new ErrorPayload($"Failed to start shell: {ex.Message}", "createTab")));
        }
    }

    private void Post(string message, object data)
    {
        if (webView == null) return;
        try
        {
            // Mendix's webView.PostMessage(message, data) emits the second arg's
            // property names verbatim — no camelCase conversion. Our DTO records
            // are PascalCase, but the JS side expects camelCase. Pre-serialize
            // through System.Text.Json with web defaults so the JsonNode that
            // Mendix forwards is already camelCase.
            var node = JsonSerializer.SerializeToNode(data, Json);
            Application.Instance.Invoke(() => webView.PostMessage(message, node!));
        }
        catch (Exception ex) { log.Error($"PostMessage({message}) failed", ex); }
    }

    private static T GetData<T>(MessageReceivedEventArgs e) where T : class
    {
        if (e.Data is null) throw new InvalidOperationException("Missing data");
        return e.Data.Deserialize<T>(Json)
            ?? throw new InvalidOperationException($"Bad payload for {typeof(T).Name}");
    }

    private string? GetProjectDir() => (getCurrentApp()?.Root as IProject)?.DirectoryPath;

    private static SettingsPayload BuildSettingsPayload(TerminalSettings s) => new(
        ShellPath: s.ShellPath,
        Args: s.Args,
        RingBufferKB: s.RingBufferKB,
        XtermScrollbackLines: s.XtermScrollbackLines,
        Theme: s.Theme,
        AvailableShells: ShellDetector.Detect()
            .Select(o => new ShellOptionPayload(o.Name, o.Path))
            .ToList(),
        McpEnabled: s.McpEnabled,
        McpPort: s.McpPort,
        McpClients: s.McpClients);
}
