using Eto.Forms;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;
using Terminal.Messages;
using System.Reflection;
using System.Text.Json;
using Terminal;
using Terminal.Interop;
using Concord.Host10x.Interop;
using Messages = Terminal.Messages;

namespace Concord.Host10x.Pane;

public sealed class TerminalPaneViewModel : WebViewDockablePaneViewModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TerminalSessionManager manager;
    private readonly Func<IModel?> getCurrentApp;
    private readonly Uri webIndexUri;
    private readonly Logger log;
    private readonly Func<string?> getApplicationRootUrl;
    private readonly string bundledSkillsRoot;
    private readonly string bundledRulesRoot;
    private readonly Func<string[]> consumePendingFirstRunNotices;
    // v4.2.1: passed through from TerminalPaneExtension so the StudioProActions
    // built during HandleSaveSettings is wired the same way as the one built
    // during TryAutoStartActionServer. Without these the get_active_run_configuration
    // tool returned "Active-run-configuration callback not wired" any time the
    // user saved Settings and the action server was rebuilt.
    private readonly Func<RunConfigurationSnapshot?>? getActiveRunConfig;
    private readonly Func<(string? path, string? name)>? getProjectInfo;

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
        Func<string?> getApplicationRootUrl,
        string bundledSkillsRoot,
        string bundledRulesRoot,
        Func<string[]> consumePendingFirstRunNotices,
        Func<RunConfigurationSnapshot?>? getActiveRunConfig = null,
        Func<(string? path, string? name)>? getProjectInfo = null)
    {
        Title = title;
        this.manager = manager;
        this.getCurrentApp = getCurrentApp;
        this.webIndexUri = webIndexUri;
        this.log = log;
        this.getApplicationRootUrl = getApplicationRootUrl;
        this.bundledSkillsRoot = bundledSkillsRoot;
        this.bundledRulesRoot = bundledRulesRoot;
        this.consumePendingFirstRunNotices = consumePendingFirstRunNotices;
        this.getActiveRunConfig = getActiveRunConfig;
        this.getProjectInfo = getProjectInfo;
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
                    Post("tabsList", new TabsListPayload(
                        manager.ListSessions().Select(s => new SessionInfoPayload(s.TabId, s.Title, s.ShellPath, s.Cwd, s.Alive)).ToList()
                    ));
                    FlushPendingFirstRunNotices();
                    break;

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
            // (via ProbeStudioProMcp). Fall back to the documented default if
            // the probe fails (SQLite locked, Studio Pro not running yet).
            // The port is NEVER persisted — it's runtime state, re-derived on
            // every save. Persisting it caused the v4.1.x bug where a fallback
            // port (8099) leaked into settings via the modal round-trip.
            var probed = ProbeStudioProMcp();
            var newPort = probed?.Port ?? TerminalSettings.StudioProMcpDefaultPort;
            var newMcpServerEnabled  = p.McpServerEnabled ?? current.McpServerEnabled;
            var newStudioProActions = p.StudioProActionsEnabled ?? current.StudioProActionsEnabled;
            var newMaiaIntegration  = p.MaiaIntegrationEnabled ?? current.MaiaIntegrationEnabled;
            var newMaiaDiagLogging = p.MaiaDiagnosticLogging ?? current.MaiaDiagnosticLogging;
            var newRefreshHotkey   = p.RefreshFromDiskHotkey ?? current.RefreshFromDiskHotkey;
            var newRestoreTabs     = p.RestoreTabsOnReopen ?? current.RestoreTabsOnReopen;

            // v4.2.0: apply the diagnostic-logging flag immediately so any CDP
            // traces during the rest of THIS save (the action-server restart
            // below re-creates a CdpClient that captures the live log) reflect
            // the user's just-saved intent, not the pre-save state.
            log.DiagnosticEnabled = newMaiaDiagLogging;

            // 1. Probe Studio Pro's primary MCP server. If unreachable, surface
            //    a notice but DO NOT abort the save — the user toggling MCP on
            //    is an intent we should persist; the connectivity is a runtime
            //    concern they can fix in Studio Pro Preferences.
            if (newEnabled)
            {
                var probe = await McpProbe.ProbeAsync(newPort, log);
                if (!probe.Ok)
                {
                    Post("mcpResult", new McpResultPayload(false,
                        "Settings saved. Studio Pro MCP didn't respond — enable it in Edit → Preferences → Maia → MCP Server, then save again.",
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
                // v4.2.1: pass the same callbacks the TryAutoStartActionServer
                // path uses so get_active_run_configuration / get_app_status
                // continue to work after a Settings save rebuilds the server.
                var actions = new StudioProActions(probe, ui,
                    getActiveRunConfig: getActiveRunConfig,
                    getProjectInfo: getProjectInfo);

                // Build Maia plumbing only on Windows when the toggle is on. The router
                // probe runs in the background; the router is functional even before it
                // returns (early calls just see all-tiers-down and fail with a clear message).
                Terminal.Maia.MaiaActions? maia = null;
                Terminal.Maia.CdpClient? sharedCdp = null;
                bool maiaEnabled = OperatingSystem.IsWindows() && newMaiaIntegration;
                if (maiaEnabled)
                {
                    // v4.2.0: singleton CdpClient (see TerminalPaneExtension.cs
                    // for the full rationale). Closure captures the same
                    // instance for both transports.
                    // v4.2.1: lifetime owned by the manager so a settings save
                    // disposes the previous client before swapping in this one.
                    sharedCdp = new Terminal.Maia.CdpClient(log);
                    var transports = new Terminal.Maia.IMaiaTransport[]
                    {
                        new Terminal.Maia.CdpInjectedTransport(() => sharedCdp),
                        new Terminal.Maia.CdpChatTransport(() => sharedCdp),
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
                    maiaIntegrationEnabled: maiaEnabled,
                    cdpClient: sharedCdp);

                // Probe the LIVE bound port (auto-fallback may have moved off
                // the default if the OS said 7783 was busy).
                var actualPort = manager.CurrentActionServerPort ?? StudioProActionServer.DefaultPort;
                var pr = await McpProbe.ProbeAsync(actualPort, log);
                if (!pr.Ok)
                {
                    manager.StopActionServer();
                    Post("mcpResult", new McpResultPayload(false,
                        $"Concord MCP didn't start on port {actualPort}. {pr.Message}",
                        Array.Empty<string>()));
                    Post("settings", BuildSettingsPayload(current));
                    return;
                }
            }
            else if (current.McpServerEnabled)
            {
                manager.StopActionServer();
            }

            var newSkillsEnabled = p.SkillsEnabled ?? current.SkillsEnabled;
            var newSkillClients  = p.SkillClients  ?? current.SkillClients;

            var updated = current with
            {
                ShellPath = p.ShellPath,
                Args = p.Args,
                RingBufferKB = p.RingBufferKB ?? current.RingBufferKB,
                XtermScrollbackLines = p.XtermScrollbackLines ?? current.XtermScrollbackLines,
                Theme = p.Theme ?? current.Theme,
                McpEnabled = newEnabled,
                McpClients = newClients,
                McpServerEnabled = newMcpServerEnabled,
                StudioProActionsEnabled = newStudioProActions,
                MaiaIntegrationEnabled = newMaiaIntegration,
                MaiaDiagnosticLogging = newMaiaDiagLogging,
                RefreshFromDiskHotkey = newRefreshHotkey,
                RestoreTabsOnReopen = newRestoreTabs,
                SkillsEnabled = newSkillsEnabled,
                SkillClients = newSkillClients,
            };

            // 3. Apply file changes BEFORE saving settings.
            var allTouched = SettingsApplyHelper.ApplyAll(
                dir,
                bundledSkillsRoot,
                bundledRulesRoot,
                current,
                updated,
                log,
                currentActionServerPort: () => manager.CurrentActionServerPort,
                probeStudioProMcpPort:   () => ProbeStudioProMcp()?.Port);

            updated.Save(dir);
            Post("settings", BuildSettingsPayload(updated));
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

    /// <summary>
    /// Build the user-facing banner shown after a Settings save. The
    /// <paramref name="touched"/> array can contain MCP-family labels
    /// (e.g. "Claude Code", "Codex actions") and skill-family labels
    /// (e.g. "Claude Code skills"). We split on the " skills" marker so
    /// each family gets the wording that matches its own enable/disable
    /// state, then join with "; " when both are present.
    /// </summary>
    private static string BuildResultMessage(TerminalSettings s, string[] touched)
    {
        var mcpTouched   = touched.Where(t => !t.Contains(" skills")).ToArray();
        var skillTouched = touched.Where(t =>  t.Contains(" skills")).ToArray();

        // Strip the " skills" suffix from skill labels so the result reads
        // cleanly under the "Skill packs:" prefix instead of repeating "skills".
        var skillNames = skillTouched
            .Select(t => t.Replace(" skills", "").Replace(" (removed)", " (removed)"))
            .ToArray();

        var parts = new List<string>();

        if (mcpTouched.Length > 0)
        {
            parts.Add(s.McpEnabled || s.McpServerEnabled
                ? $"MCP wired for {string.Join(", ", mcpTouched)}"
                : $"MCP removed for {string.Join(", ", mcpTouched)}");
        }

        if (skillNames.Length > 0)
        {
            parts.Add(s.SkillsEnabled
                ? $"Skill packs installed: {string.Join(", ", skillNames)}"
                : $"Skill packs removed: {string.Join(", ", skillNames)}");
        }

        return parts.Count == 0
            ? "Saved. Restarting terminals…"
            : string.Join("; ", parts) + ". Restarting terminals…";
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

    /// <summary>
    /// Pull queued first-run notices from the extension and surface each one
    /// as an <c>mcpResult</c> banner. Idempotent — the consume-Func clears
    /// the queue on first call, so subsequent invocations no-op.
    /// </summary>
    private void FlushPendingFirstRunNotices()
    {
        try
        {
            var notices = consumePendingFirstRunNotices();
            foreach (var notice in notices)
            {
                Post("mcpResult", new Messages.McpResultPayload(true, notice, Array.Empty<string>()));
                log.Info($"[first-run] flushed notice: {notice}");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"[first-run] flush failed: {ex.Message}");
        }
    }

    private SettingsPayload BuildSettingsPayload(TerminalSettings s)
    {
        var dir = GetProjectDir();
        var settingsPath = dir != null
            ? System.IO.Path.Combine(dir, "resources", "terminal-settings.json")
            : null;

        var bundled = BundledSkillReader.Enumerate(bundledSkillsRoot)
            .Select(b => new BundledSkillPayload(b.Name, b.Description))
            .ToList();

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
            McpClients: s.McpClients,
            McpServerEnabled: s.McpServerEnabled,
            StudioProActionsEnabled: s.StudioProActionsEnabled,
            MaiaIntegrationEnabled: s.MaiaIntegrationEnabled,
            MaiaDiagnosticLogging: s.MaiaDiagnosticLogging,
            Platform: OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : "linux",
            RefreshFromDiskHotkey: s.RefreshFromDiskHotkey,
            RestoreTabsOnReopen: s.RestoreTabsOnReopen,
            About: new AboutInfoPayload(
                Version: ResolveBuildVersion(),
                LogPath: log?.Path,
                SettingsPath: settingsPath),
            StudioProMcp: ProbeStudioProMcp(),
            // The live bound port — null when the bridge isn't running.
            // Display-only; never echoed back through SaveSettings (that
            // round-trip is exactly what caused the 8099 leak in v4.1.x).
            LiveActionServerPort: manager.CurrentActionServerPort,
            SkillsEnabled: s.SkillsEnabled,
            SkillClients: s.SkillClients,
            BundledSkills: bundled);
    }

    /// <summary>
    /// Try to read Studio Pro's own MCP-server preference (from Settings.sqlite)
    /// so the JS side can warn when our saved port differs from what Studio Pro
    /// is actually serving on. Returns null on any probe failure.
    /// <para>
    /// Note: Host10x port — StudioProVersionFromExePath() is inlined here as a
    /// private static rather than delegating to TerminalPaneExtension (which is
    /// ported in Task 24). The logic is identical: extract major.minor.patch
    /// from the current process exe path via regex.
    /// </para>
    /// </summary>
    private StudioProMcpInfoPayload? ProbeStudioProMcp()
    {
        try
        {
            var version = StudioProVersionFromExePath();
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

    /// <summary>
    /// Extracts a "major.minor.patch" version from Studio Pro's process exe path.
    /// Works for both Windows (...\Mendix\11.10.0\modeler\studiopro.exe) and the
    /// Mac bundle layout (e.g. /Applications/Mendix Studio Pro 11.10.0.app/...).
    /// Returns null if the path doesn't contain a version triple.
    /// <para>
    /// Inlined from TerminalPaneExtension (Host11x) — Task 24 will port
    /// TerminalPaneExtension and can expose this as internal static again if
    /// the two call sites want to share. For now, the three-line body is not
    /// worth a forward dependency.
    /// </para>
    /// </summary>
    private static string? StudioProVersionFromExePath()
    {
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(exePath, @"\d+\.\d+\.\d+");
        return match.Success ? match.Value : null;
    }

}
