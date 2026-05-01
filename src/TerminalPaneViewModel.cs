using Eto.Forms;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;
using Terminal.Messages;
using System.Reflection;
using System.Text.Json;

namespace Terminal;

public sealed class TerminalPaneViewModel : WebViewDockablePaneViewModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TerminalSessionManager manager;
    private readonly Func<IModel?> getCurrentApp;
    private readonly Uri webIndexUri;
    private readonly Logger log;
    private readonly Func<string?> getApplicationRootUrl;

    private IWebView? webView;
    private Action<string, byte[]>? outputHandler;
    private Action<string, int?>?  exitedHandler;

    public TerminalPaneViewModel(
        string title,
        TerminalSessionManager manager,
        Func<IModel?> getCurrentApp,
        Uri webIndexUri,
        Logger log,
        Func<string?> getApplicationRootUrl)
    {
        Title = title;
        this.manager = manager;
        this.getCurrentApp = getCurrentApp;
        this.webIndexUri = webIndexUri;
        this.log = log;
        this.getApplicationRootUrl = getApplicationRootUrl;
    }

    public override void InitWebView(IWebView webView)
    {
        this.webView = webView;
        webView.MessageReceived += OnWebViewMessage;
        webView.Address = webIndexUri;
        // Allow right-click → Inspect inside the WebView for diagnostics.
        try { ((dynamic)webView).AllowedDevTools = true; } catch { /* best-effort */ }
        try { ((dynamic)webView).AllowReload    = true; } catch { /* best-effort */ }
        log.Info($"InitWebView build={ResolveBuildVersion()} at {webIndexUri}");

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

                case "diag":
                {
                    try
                    {
                        var msg = e.Data?["msg"]?.ToString() ?? "(no msg)";
                        log.Info($"JS: {msg}");
                    }
                    catch (Exception ex) { log.Warn($"diag failed: {ex.Message}"); }
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

            var newClients         = p.McpClients ?? current.McpClients;
            var newEnabled         = p.McpEnabled ?? current.McpEnabled;
            var newPort            = p.McpPort    ?? current.McpPort;
            var newActionsEnabled  = p.ActionsServerEnabled ?? current.ActionsServerEnabled;
            var newActionsPort     = p.ActionsServerPort    ?? current.ActionsServerPort;
            var newRefreshHotkey   = p.RefreshFromDiskHotkey ?? current.RefreshFromDiskHotkey;
            var newRestoreTabs     = p.RestoreTabsOnReopen ?? current.RestoreTabsOnReopen;

            // 1. Probe Studio Pro's primary MCP server (existing behaviour).
            if (newEnabled)
            {
                var probe = await McpProbe.ProbeAsync(newPort, log);
                if (!probe.Ok)
                {
                    Post("mcpResult", new McpResultPayload(false,
                        $"{probe.Message}. Enable Studio Pro's MCP server in Preferences → Maia → MCP Server, then try again.",
                        Array.Empty<string>()));
                    Post("settings", BuildSettingsPayload(current));
                    return;
                }
            }

            // 2. Manage our own action-server lifecycle.
            if (newActionsEnabled)
            {
                // Re-create on each save when port or hotkey changed; StartActionServer is idempotent on no-op.
                var ui = new StudioProUiAutomation(
                    runHotkey: "F5",
                    stopHotkey: "Shift+F5",
                    refreshHotkey: newRefreshHotkey,
                    log: log);
                var probe = new RunStateProbe(getApplicationRootUrl);
                var actions = new StudioProActions(probe, ui);
                manager.StartActionServer(newActionsPort, actions, log);

                // Probe our own server. Re-use McpProbe since wire formats match.
                var pr = await McpProbe.ProbeAsync(newActionsPort, log);
                if (!pr.Ok)
                {
                    manager.StopActionServer();
                    Post("mcpResult", new McpResultPayload(false,
                        $"Action server failed to answer on port {newActionsPort}: {pr.Message}",
                        Array.Empty<string>()));
                    Post("settings", BuildSettingsPayload(current));
                    return;
                }
            }
            else if (current.ActionsServerEnabled)
            {
                manager.StopActionServer();
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
                ActionsServerEnabled = newActionsEnabled,
                ActionsServerPort = newActionsPort,
                RefreshFromDiskHotkey = newRefreshHotkey,
                RestoreTabsOnReopen = newRestoreTabs,
            };

            // 3. Apply file changes BEFORE saving settings.
            var touchedPrimary = ApplyMcpConfig(dir, current, updated);
            var touchedActions = ApplyActionsMcpConfig(dir, current, updated);

            updated.Save(dir);
            Post("settings", BuildSettingsPayload(updated));

            var allTouched = touchedPrimary.Concat(touchedActions).ToArray();
            if (allTouched.Length > 0)
            {
                Post("mcpResult", new McpResultPayload(true, BuildResultMessage(updated, allTouched), allTouched));

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
                    catch (Exception ex) { log.Error("RecycleAll after MCP save failed", ex); }
                });
            }
        }
        catch (Exception ex)
        {
            log.Error("SaveSettings failed", ex);
            Post("error", new ErrorPayload($"Save failed: {ex.Message}", "saveSettings"));
        }
    }

    private static string BuildResultMessage(TerminalSettings s, string[] touched) =>
        (s.McpEnabled || s.ActionsServerEnabled)
            ? $"MCP servers updated for {string.Join(", ", touched)}. Restarting open terminals…"
            : $"MCP servers disabled (cleaned up: {string.Join(", ", touched)}). Restarting open terminals…";

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

        // Always use Studio Pro's actual MCP port (probed live each save).
        // The saved next.McpPort is back-compat only — no longer user-settable.
        // Falls back to the saved value if the probe fails, keeping legacy
        // configs working until the user enables Studio Pro's MCP.
        var probedPort = ProbeStudioProMcp()?.Port ?? next.McpPort;
        var url = $"http://localhost:{probedPort}/mcp";
        var json = new McpJsonConfigurator(projectDir);
        var toml = new McpTomlConfigurator();
        var touched = new List<string>();

        if (jsonNeeded) { json.Upsert(url); touched.Add(LabelForJson(nextClients)); }
        else if (jsonHadIt) { json.Remove(); touched.Add(LabelForJson(prevClients) + " (removed)"); }

        if (tomlNeeded) { toml.Upsert(url); touched.Add("Codex"); }
        else if (tomlHadIt) { toml.Remove(); touched.Add("Codex (removed)"); }

        return touched.ToArray();
    }

    /// <summary>
    /// Diff the actions-server toggle (and which CLIs are selected via the existing
    /// McpClients list) and write the second server entry into .mcp.json / config.toml.
    /// We piggy-back on McpClients — the action server is registered for the same
    /// CLIs the user already chose in the primary MCP toggle.
    /// </summary>
    private string[] ApplyActionsMcpConfig(string projectDir, TerminalSettings prev, TerminalSettings next)
    {
        var prevClients = new HashSet<string>(prev.McpClients, StringComparer.OrdinalIgnoreCase);
        var nextClients = next.ActionsServerEnabled
            ? new HashSet<string>(next.McpClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var jsonNeeded = nextClients.Contains("claude") || nextClients.Contains("copilot");
        var jsonHadIt  = prev.ActionsServerEnabled && (prevClients.Contains("claude") || prevClients.Contains("copilot"));
        var tomlNeeded = nextClients.Contains("codex");
        var tomlHadIt  = prev.ActionsServerEnabled && prevClients.Contains("codex");

        var url = $"http://localhost:{next.ActionsServerPort}/mcp";
        var json = new McpJsonConfigurator(projectDir);
        var toml = new McpTomlConfigurator();
        var touched = new List<string>();

        if (jsonNeeded) { json.UpsertActions(url); touched.Add(LabelForJson(nextClients) + " actions"); }
        else if (jsonHadIt) { json.RemoveActions(); touched.Add(LabelForJson(prevClients) + " actions (removed)"); }

        if (tomlNeeded) { toml.UpsertActions(url); touched.Add("Codex actions"); }
        else if (tomlHadIt) { toml.RemoveActions(); touched.Add("Codex actions (removed)"); }

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

            var (tabId, title) = await manager.CreateSessionAsync(shell, args, cwd, p.Cols, p.Rows);
            Post("tabCreated", new TabCreatedPayload(tabId, title, shell, cwd));
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

    /// <summary>
    /// Pulls the build version from assembly metadata so logs (and the
    /// future "About" surface in the settings modal) read a single source
    /// of truth set in Terminal.csproj's &lt;InformationalVersion&gt;.
    /// </summary>
    private static string ResolveBuildVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info)) return $"v{info}";
        var v = asm.GetName().Version;
        return v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v?";
    }

    private SettingsPayload BuildSettingsPayload(TerminalSettings s)
    {
        var dir = GetProjectDir();
        var settingsPath = dir != null
            ? System.IO.Path.Combine(dir, "resources", "terminal-settings.json")
            : null;
        return new SettingsPayload(
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
            McpClients: s.McpClients,
            ActionsServerEnabled: s.ActionsServerEnabled,
            ActionsServerPort: s.ActionsServerPort,
            RefreshFromDiskHotkey: s.RefreshFromDiskHotkey,
            RestoreTabsOnReopen: s.RestoreTabsOnReopen,
            About: new AboutInfoPayload(
                Version: ResolveBuildVersion(),
                LogPath: log?.Path,
                SettingsPath: settingsPath),
            StudioProMcp: ProbeStudioProMcp());
    }

    /// <summary>
    /// Try to read Studio Pro's own MCP-server preference (from Settings.sqlite)
    /// so the JS side can warn when our saved port differs from what Studio Pro
    /// is actually serving on. Returns null on any probe failure.
    /// </summary>
    private static StudioProMcpInfoPayload? ProbeStudioProMcp()
    {
        try
        {
            var sp = System.Diagnostics.Process.GetCurrentProcess();
            var exePath = sp.MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return null;
            var versionDir = new FileInfo(exePath).Directory?.Parent?.Name;
            if (string.IsNullOrEmpty(versionDir)) return null;
            var info = StudioProThemeProbe.ReadMcpServer(versionDir);
            return new StudioProMcpInfoPayload(info.Enabled, info.Port);
        }
        catch { return null; }
    }
}
