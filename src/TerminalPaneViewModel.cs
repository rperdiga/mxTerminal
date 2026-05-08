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
    /// <summary>
    /// Last project dir we successfully resolved from the Mendix API.
    /// Used as a fallback in HandleCreateTab when the live lookup
    /// momentarily returns null — without this we'd silently fall through
    /// to Environment.CurrentDirectory (Studio Pro's .app bundle on Mac),
    /// which is wrong for shell sessions.
    /// </summary>
    private string? lastKnownProjectDir;

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

        // Manager output / exit subscriptions are owned by TerminalPaneExtension
        // — see EnsureManagerForwardingHooked there. The extension forwards each
        // event to whichever view model is currently active via PostOutput /
        // PostExit. We deliberately do NOT subscribe to OnClosed here:
        // Mendix's DockablePane DisposeWindow walks OnClosed subscribers
        // during shutdown, and any delegate pointing into our (about-to-be-
        // unloaded) IL has triggered BadImageFormatException ("Bad IL range")
        // popups — even when the body itself was wrapped in try/catches.
        // With no subscription, Mendix has nothing of ours to invoke.
    }

    /// <summary>
    /// Forward a PTY output frame to the JS side. Called by
    /// <c>TerminalPaneExtension</c>'s manager-event forwarder.
    /// No-op once the WebView has been disposed.
    /// </summary>
    public void PostOutput(string tabId, byte[] bytes) =>
        Post("output", new OutputPayload(tabId, Convert.ToBase64String(bytes)));

    /// <summary>
    /// Forward a PTY exit notice to the JS side. Called by
    /// <c>TerminalPaneExtension</c>'s manager-event forwarder.
    /// No-op once the WebView has been disposed.
    /// </summary>
    public void PostExit(string tabId, int? code) =>
        Post("exit", new ExitPayload(tabId, code));

    /// <summary>
    /// Mark this view model as no longer the active forwarding target.
    /// After this returns, <see cref="Post"/> short-circuits because
    /// <c>webView</c> is null, so any straggling event from the manager
    /// silently no-ops instead of touching a disposed WebView.
    /// </summary>
    public void DetachLifecycleHooks()
    {
        try { if (webView != null) webView.MessageReceived -= OnWebViewMessage; } catch { /* ignore */ }
        webView = null;
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
                    // Diagnostic — only log non-keystroke inputs (paste, multi-byte sequences)
                    // to avoid one-log-per-keypress flood during interactive typing.
                    if (bytes.Length > 32)
                    {
                        var preview = bytes.Length <= 64
                            ? Convert.ToHexString(bytes)
                            : Convert.ToHexString(bytes.AsSpan(0, 32).ToArray()) + "..." + Convert.ToHexString(bytes.AsSpan(bytes.Length - 8).ToArray());
                        log.Info($"input tab={p.TabId.Substring(0, 8)} len={bytes.Length} preview={preview}");
                    }
                    _ = manager.Write(p.TabId, bytes);
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
            // Always derive the MCP port from Studio Pro's live Settings.sqlite
            // (via ProbeStudioProMcp) — never trust the user-supplied / saved
            // value, which can drift when the user opens the same Concord on a
            // project that hasn't enabled Studio Pro's MCP yet (legacy default
            // 7782 from TerminalSettings.Defaults causes the connection-timeout
            // banner Neo hit on ConcordPublisher 2026-05-02). Fall back to the
            // saved port only if the probe fails entirely (e.g. SQLite locked).
            var probed = ProbeStudioProMcp();
            var newPort = probed?.Port ?? p.McpPort ?? current.McpPort;
            var newMcpServerEnabled  = p.McpServerEnabled ?? current.McpServerEnabled;
            var newMcpServerPort     = p.McpServerPort   ?? current.McpServerPort;
            var newStudioProActions = p.StudioProActionsEnabled ?? current.StudioProActionsEnabled;
            var newMaiaIntegration  = p.MaiaIntegrationEnabled ?? current.MaiaIntegrationEnabled;
            var newRefreshHotkey   = p.RefreshFromDiskHotkey ?? current.RefreshFromDiskHotkey;
            var newRestoreTabs     = p.RestoreTabsOnReopen ?? current.RestoreTabsOnReopen;

            // 1. Probe Studio Pro's primary MCP server. If unreachable, surface
            //    a notice but DO NOT abort the save — the user toggling MCP on
            //    is an intent we should persist; the connectivity is a runtime
            //    concern they can fix in Studio Pro Preferences without losing
            //    their Concord settings.
            if (newEnabled)
            {
                var probe = await McpProbe.ProbeAsync(newPort, log);
                if (!probe.Ok)
                {
                    Post("mcpResult", new McpResultPayload(false,
                        $"Settings saved, but Studio Pro's MCP server didn't answer on port {newPort}. Enable it in Preferences -> Maia -> MCP Server, then re-save to wire up the CLI configs.",
                        Array.Empty<string>()));
                }
            }

            // 2. Manage our own action-server lifecycle.
            if (newMcpServerEnabled)
            {
                // Re-create on each save when port or hotkey changed; StartActionServer is idempotent on no-op.
                var ui = new StudioProUiAutomation(
                    runHotkey: "F5",
                    stopHotkey: "Shift+F5",
                    refreshHotkey: newRefreshHotkey,
                    log: log);
                var probe = new RunStateProbe(getApplicationRootUrl);
                var actions = new StudioProActions(probe, ui);

                // Build Maia plumbing only on Windows when the toggle is on. The router
                // probe runs in the background; the router is functional even before it
                // returns (early calls just see all-tiers-down and fail with a clear message).
                Terminal.Maia.MaiaActions? maia = null;
                bool maiaEnabled = OperatingSystem.IsWindows() && newMaiaIntegration;
                if (maiaEnabled)
                {
                    var transports = new Terminal.Maia.IMaiaTransport[]
                    {
                        new Terminal.Maia.CdpInjectedTransport(() => new Terminal.Maia.CdpClient()),
                        new Terminal.Maia.CdpChatTransport(() => new Terminal.Maia.CdpClient()),
                    };
                    var router = new Terminal.Maia.MaiaRouter(transports);
                    _ = router.ProbeAllAsync(CancellationToken.None);  // fire-and-forget; safe
                    maia = new Terminal.Maia.MaiaActions(router);
                }

                manager.StartActionServer(
                    StudioProActionServer.DefaultPort,
                    actions,
                    log,
                    maia,
                    studioProActionsEnabled: newStudioProActions,
                    maiaIntegrationEnabled: maiaEnabled);

                // Probe the LIVE bound port (auto-fallback may have moved off
                // the default if the OS said 7783 was busy).
                var actualPort = manager.CurrentActionServerPort ?? StudioProActionServer.DefaultPort;
                var pr = await McpProbe.ProbeAsync(actualPort, log);
                if (!pr.Ok)
                {
                    manager.StopActionServer();
                    Post("mcpResult", new McpResultPayload(false,
                        $"Action server failed to answer on port {actualPort}: {pr.Message}",
                        Array.Empty<string>()));
                    Post("settings", BuildSettingsPayload(current));
                    return;
                }
            }
            else if (current.McpServerEnabled)
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
                McpServerEnabled = newMcpServerEnabled,
                McpServerPort = newMcpServerPort,
                StudioProActionsEnabled = newStudioProActions,
                MaiaIntegrationEnabled = newMaiaIntegration,
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
        (s.McpEnabled || s.McpServerEnabled)
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

        log.Info($"[mcp-config] primary diff jsonNeeded={jsonNeeded} jsonHadIt={jsonHadIt} tomlNeeded={tomlNeeded} tomlHadIt={tomlHadIt} url={url}");

        try
        {
            if (jsonNeeded) { json.Upsert(url); log.Info($"[mcp-config-json] upserted {McpJsonConfigurator.ServerName} -> {url}"); touched.Add(LabelForJson(nextClients)); }
            else if (jsonHadIt) { json.Remove(); log.Info($"[mcp-config-json] removed {McpJsonConfigurator.ServerName}"); touched.Add(LabelForJson(prevClients) + " (removed)"); }
        }
        catch (Exception ex) { log.Error("[mcp-config-json] primary write failed", ex); }

        try
        {
            if (tomlNeeded) { toml.Upsert(url); log.Info($"[mcp-config-toml] upserted {McpTomlConfigurator.ServerName} -> {url} at {toml.FilePath}"); touched.Add("Codex"); }
            else if (tomlHadIt) { toml.Remove(); log.Info($"[mcp-config-toml] removed {McpTomlConfigurator.ServerName}"); touched.Add("Codex (removed)"); }
        }
        catch (Exception ex) { log.Error("[mcp-config-toml] primary write failed", ex); }

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
        var nextClients = next.McpServerEnabled
            ? new HashSet<string>(next.McpClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var jsonNeeded = nextClients.Contains("claude") || nextClients.Contains("copilot");
        var jsonHadIt  = prev.McpServerEnabled && (prevClients.Contains("claude") || prevClients.Contains("copilot"));
        var tomlNeeded = nextClients.Contains("codex");
        var tomlHadIt  = prev.McpServerEnabled && prevClients.Contains("codex");

        // Use the LIVE bound port (manager surfaces it after Start). The
        // saved value is back-compat fallback only — bridge auto-binds at
        // 7783 with free-port fallback; user can't choose anymore.
        var port = manager.CurrentActionServerPort ?? next.McpServerPort;
        var url = $"http://localhost:{port}/mcp";
        var json = new McpJsonConfigurator(projectDir);
        var toml = new McpTomlConfigurator();
        var touched = new List<string>();

        log.Info($"[mcp-config] actions diff jsonNeeded={jsonNeeded} jsonHadIt={jsonHadIt} tomlNeeded={tomlNeeded} tomlHadIt={tomlHadIt} url={url} live-port={manager.CurrentActionServerPort?.ToString() ?? "null"}");

        try
        {
            if (jsonNeeded) { json.UpsertActions(url); log.Info($"[mcp-config-json] upserted {McpJsonConfigurator.ActionsServerName} -> {url}"); touched.Add(LabelForJson(nextClients) + " actions"); }
            else if (jsonHadIt) { json.RemoveActions(); log.Info($"[mcp-config-json] removed {McpJsonConfigurator.ActionsServerName}"); touched.Add(LabelForJson(prevClients) + " actions (removed)"); }
        }
        catch (Exception ex) { log.Error("[mcp-config-json] actions write failed", ex); }

        try
        {
            if (tomlNeeded) { toml.UpsertActions(url); log.Info($"[mcp-config-toml] upserted {McpTomlConfigurator.ActionsServerName} -> {url} at {toml.FilePath}"); touched.Add("Codex actions"); }
            else if (tomlHadIt) { toml.RemoveActions(); log.Info($"[mcp-config-toml] removed {McpTomlConfigurator.ActionsServerName}"); touched.Add("Codex actions (removed)"); }
        }
        catch (Exception ex) { log.Error("[mcp-config-toml] actions write failed", ex); }

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
            // Resolve the cwd in priority order: explicit JS payload, live
            // project dir, last-known project dir (cached when Mendix has
            // briefly dropped the model), HOME. NEVER fall through to
            // Environment.CurrentDirectory — on macOS that is Studio Pro's
            // .app bundle, and a shell started there causes Claude/Codex
            // to register the .app as the "project" so .mcp.json (which
            // lives in the real project root) is invisible. Verified
            // 2026-05-07: claude.json had a project key
            // /Applications/Mendix Studio Pro 11.10.0 Beta.app from a
            // previous bad cwd.
            var liveDir = GetProjectDir();
            var dir = p.Cwd
                      ?? liveDir
                      ?? lastKnownProjectDir
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (liveDir != null) lastKnownProjectDir = liveDir;
            var settings = TerminalSettings.Load(liveDir ?? lastKnownProjectDir ?? "");
            var shell = p.ShellPath ?? settings.ShellPath;
            var args = p.Args ?? settings.Args;
            var cwd = dir;

            log.Info($"[create-tab] cwd={cwd} (live={liveDir ?? "null"} cached={lastKnownProjectDir ?? "null"} payload={p.Cwd ?? "null"}) shell={shell}");
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
        if (!string.IsNullOrEmpty(info))
        {
            // Strip the +metadata segment (build label, git hash, etc.) so the
            // visible version is just the SemVer triple — readable and stable
            // across rebuilds. The full string is still in logs if we need it.
            var plus = info.IndexOf('+');
            var clean = plus > 0 ? info.Substring(0, plus) : info;
            return $"v{clean}";
        }
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
            McpServerEnabled: s.McpServerEnabled,
            // Report the LIVE bound port when the bridge is running so the JS
            // readout shows the truth (auto-fallback may have moved off 7783).
            McpServerPort: manager.CurrentActionServerPort ?? s.McpServerPort,
            StudioProActionsEnabled: s.StudioProActionsEnabled,
            MaiaIntegrationEnabled: s.MaiaIntegrationEnabled,
            Platform: OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : "linux",
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
    private StudioProMcpInfoPayload? ProbeStudioProMcp()
    {
        try
        {
            var version = TerminalPaneExtension.StudioProVersionFromExePath();
            if (string.IsNullOrEmpty(version))
            {
                log.Info("[mcp-probe] version not detected from exe path");
                return null;
            }
            var info = StudioProThemeProbe.ReadMcpServer(version);
            log.Info($"[mcp-probe] sp-version={version} {info.Diagnostic}");
            return new StudioProMcpInfoPayload(info.Enabled, info.Port);
        }
        catch (Exception ex)
        {
            log.Warn($"[mcp-probe] outer exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

}
