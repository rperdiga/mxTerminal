using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.Events;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;

namespace Terminal;

[Export(typeof(DockablePaneExtension))]
public sealed class TerminalPaneExtension : DockablePaneExtension
{
    // Product name. Used by Studio Pro both as the pane's MEF identity AND
    // the visible tab title in the right-pane strip. Renamed from "Terminal"
    // to "Concord" — see About panel for the meaning.
    public const string ID = "Concord";
    public override string Id => ID;

    private readonly TerminalSessionManager manager;
    private readonly ILocalRunConfigurationsService localRunConfigs;
    private Logger log = null!;
    private bool subscribed;
    private bool stateRestored;          // first-Open guard for cross-session restore
    private bool sessionsChangedHooked;  // first-Open guard for persistence hook
    // Captured on UI-thread during Open(); SaveStateBestEffort runs from the
    // SessionsChanged event which may fire on a non-UI thread where
    // CurrentApp can return null.
    private string? cachedProjectDir;

    [ImportingConstructor]
    public TerminalPaneExtension(ILocalRunConfigurationsService localRunConfigs)
    {
        this.localRunConfigs = localRunConfigs;
        manager = new TerminalSessionManager(new PtyNetFactory());
    }

    public override DockablePaneViewModelBase Open()
    {
        EnsureLogger();
        // Cache project dir while we're on the UI thread; SaveStateBestEffort
        // runs from the SessionsChanged event off-thread.
        cachedProjectDir = (CurrentApp?.Root as IProject)?.DirectoryPath ?? cachedProjectDir;
        EnsureLifecycleSubscribed();
        EnsureStatePersistenceHooked();
        TryAutoStartActionServer();
        TryRestoreTabsOnFirstOpen();

        // Append ?theme=dark|light from Studio Pro's persisted preference
        // (SQLite probe) so the WebView gets the host's actual theme — NOT
        // the OS app theme that prefers-color-scheme would otherwise return.
        var indexUri = new Uri(WebServerBaseUrl, "index.html");
        var themeQuery = ResolveThemeQuery();
        if (themeQuery != null)
            indexUri = new Uri($"{indexUri}?theme={themeQuery}");
        log?.Info($"InitWebView indexUri={indexUri} theme-from-probe={themeQuery ?? "<none>"}");
        return new TerminalPaneViewModel(
            title: "Concord",
            manager: manager,
            getCurrentApp: () => CurrentApp,
            webIndexUri: indexUri,
            log: log,
            getApplicationRootUrl: () =>
            {
                var model = CurrentApp;
                if (model is null) return null;
                try { return localRunConfigs.GetActiveConfiguration(model)?.ApplicationRootUrl; }
                catch (Exception ex) { log?.Warn($"GetActiveConfiguration threw: {ex.Message}"); return null; }
            });
    }

    private void EnsureLogger()
    {
        var dir = (CurrentApp?.Root as IProject)?.DirectoryPath ?? Environment.CurrentDirectory;
        log = new Logger(dir);
    }

    private void EnsureLifecycleSubscribed()
    {
        if (subscribed) return;
        subscribed = true;
        Subscribe<ExtensionUnloading>(() =>
        {
            try { log.Info("ExtensionUnloading — disposing all PTYs and action server"); manager.Dispose(); }
            catch (Exception ex) { log.Error("Dispose on unload failed", ex); }
        });
    }

    /// <summary>
    /// Subscribe once to the manager's SessionsChanged event so any tab
    /// create/close/exit writes the new state to disk. Setting takes effect
    /// regardless of whether RestoreTabsOnReopen is currently enabled —
    /// flipping the setting on later then reads the most recent state.
    /// </summary>
    private void EnsureStatePersistenceHooked()
    {
        if (sessionsChangedHooked) return;
        sessionsChangedHooked = true;
        manager.SessionsChanged += SaveStateBestEffort;
    }

    private void SaveStateBestEffort()
    {
        try
        {
            // Prefer the dir cached at Open() time (UI thread). Fall back to
            // CurrentApp only if cache is somehow null — extension reload edge case.
            var dir = cachedProjectDir ?? (CurrentApp?.Root as IProject)?.DirectoryPath;
            if (dir is null) { log?.Warn("[state] save skipped: no project dir"); return; }
            var snap = manager.SnapshotState();
            snap.Save(dir);
            log?.Info($"[state] saved {snap.Tabs.Count} tab(s) to {dir}");
        }
        catch (Exception ex) { log?.Warn($"[state] save failed: {ex.Message}"); }
    }

    /// <summary>
    /// First time the pane opens this Studio Pro session, if the manager
    /// has zero sessions AND restore-on-reopen is enabled AND a saved state
    /// exists, silently re-spawn each saved tab (skipping any that exited
    /// cleanly last session). Edge cases:
    ///  - Saved shell missing → fall back to settings.ShellPath, log warning
    ///    but keep the saved title so the user recognizes the tab.
    ///  - No project context yet → skip; we'll never restore in that case.
    /// </summary>
    private async void TryRestoreTabsOnFirstOpen()
    {
        if (stateRestored) return;
        stateRestored = true;
        try
        {
            var dir = cachedProjectDir ?? (CurrentApp?.Root as IProject)?.DirectoryPath;
            if (dir is null) return;
            if (manager.SessionCount > 0) return;
            var settings = TerminalSettings.Load(dir);
            if (!settings.RestoreTabsOnReopen) return;
            var state = TerminalState.Load(dir);
            if (state.Tabs.Count == 0) return;

            log.Info($"[state] restoring {state.Tabs.Count} tab(s)");
            foreach (var t in state.Tabs)
            {
                var shell = File.Exists(t.ShellPath) ? t.ShellPath : settings.ShellPath;
                if (shell != t.ShellPath)
                    log.Warn($"[state] saved shell {t.ShellPath} not found; falling back to {shell}");
                try
                {
                    await manager.CreateSessionAsync(shell, t.Args, dir, 80, 24);
                }
                catch (Exception ex) { log.Warn($"[state] failed to restore {t.Title}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { log?.Warn($"[state] restore failed: {ex.Message}"); }
    }

    private void TryAutoStartActionServer()
    {
        var dir = (CurrentApp?.Root as IProject)?.DirectoryPath;
        if (dir is null) return;
        var settings = TerminalSettings.Load(dir);
        if (!settings.ActionsServerEnabled) return;

        try
        {
            var ui = new StudioProUiAutomation(
                runHotkey: "F5",
                stopHotkey: "Shift+F5",
                refreshHotkey: settings.RefreshFromDiskHotkey,
                log: log);
            var probe = new RunStateProbe(() =>
            {
                var model = CurrentApp;
                if (model is null) return null;
                try { return localRunConfigs.GetActiveConfiguration(model)?.ApplicationRootUrl; }
                catch (Exception ex) { log?.Warn($"GetActiveConfiguration threw: {ex.Message}"); return null; }
            });
            var actions = new StudioProActions(
                probe, ui,
                getActiveRunConfig: () =>
                {
                    var model = CurrentApp;
                    if (model is null) return null;
                    try
                    {
                        var c = localRunConfigs.GetActiveConfiguration(model);
                        if (c is null) return null;
                        // Use reflection-friendly property access via dynamic; Mendix's
                        // service contract may evolve and we want to fail soft, not hard.
                        dynamic d = c;
                        string? id  = TryStr(() => (string?)d.Id?.ToString());
                        string? nm  = TryStr(() => (string?)d.Name);
                        string? url = TryStr(() => (string?)d.ApplicationRootUrl);
                        return new RunConfigurationInfo(id, nm, url);
                    }
                    catch (Exception ex) { log?.Warn($"getActiveRunConfig threw: {ex.Message}"); return null; }
                },
                getProjectInfo: () =>
                {
                    var proj = CurrentApp?.Root as IProject;
                    return (proj?.DirectoryPath, proj?.Name);
                });
            // Fixed default — saved settings.ActionsServerPort is ignored.
            // The server falls back to a free OS-assigned port if 7783 is taken.
            manager.StartActionServer(StudioProActionServer.DefaultPort, actions, log);
            log.Info($"[actions] auto-started server on port {settings.ActionsServerPort}");
        }
        catch (Exception ex)
        {
            log.Error("[actions] auto-start failed", ex);
        }
    }

    private static string? TryStr(Func<string?> f)
    {
        try { return f(); } catch { return null; }
    }

    /// <summary>
    /// Look up Studio Pro's persisted theme preference and convert to a URL
    /// query value ("dark" or "light"). Returns null when the probe fails for
    /// any reason — the JS side falls back to prefers-color-scheme.
    /// </summary>
    private string? ResolveThemeQuery()
    {
        try
        {
            // Studio Pro's process file path looks like:
            //   C:\DevTools\Mendix\11.10.0\modeler\studiopro.exe
            // The version dir is the parent of "modeler" — most reliable signal.
            var sp = System.Diagnostics.Process.GetCurrentProcess();
            var exePath = sp.MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return null;
            // ...\Mendix\<version>\modeler\studiopro.exe → grandparent's name = "<version>"
            var versionDir = new FileInfo(exePath).Directory?.Parent?.Name;
            if (string.IsNullOrEmpty(versionDir))
            {
                log?.Info("[theme-probe] version-dir not detected from exe path");
                return null;
            }
            var result = StudioProThemeProbe.Read(versionDir);
            log?.Info($"[theme-probe] sp-version={versionDir} {result.Diagnostic}");
            return result.Theme is null ? null : StudioProThemeProbe.ToUrlValue(result.Theme.Value);
        }
        catch (Exception ex)
        {
            log?.Warn($"[theme-probe] outer exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
