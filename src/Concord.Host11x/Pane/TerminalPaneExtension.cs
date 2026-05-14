using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.Events;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using MxVcsService = Mendix.StudioPro.ExtensionsAPI.UI.Services.IVersionControlService;
using Terminal;
using Terminal.Interop;
using Concord.Host11x;
using Concord.Host11x.Interop;

namespace Concord.Host11x.Pane;

[Export(typeof(DockablePaneExtension))]
public sealed class TerminalPaneExtension : DockablePaneExtension
{
    [Import(typeof(Host11xEntry))]
#pragma warning disable CS0414  // Sentinel: field is read by MEF activation, never used by host code
    private Host11xEntry? _entry = null;
#pragma warning restore CS0414

    // Product name. Used by Studio Pro both as the pane's MEF identity AND
    // the visible tab title in the right-pane strip. Renamed from "Terminal"
    // to "Concord" — see About panel for the meaning.
    public const string ID = "Concord";
    public override string Id => ID;

    private readonly TerminalSessionManager manager;
    private readonly ILocalRunConfigurationsService localRunConfigs;
    // MEF-imported services consumed by the 7 model-tier Interop hosts wired
    // in TryAutoStartActionServer. IPageGenerationService, INavigationManagerService,
    // and IMicroflowService are required (their corresponding hosts have non-nullable
    // ctor params); the rest are optional and tolerate missing exports via AllowDefault.
    private readonly IPageGenerationService pageGenerationService;
    private readonly INavigationManagerService navigationManagerService;
    private readonly IMicroflowService microflowService;
    private readonly INameValidationService? nameValidationService;
    private readonly IUntypedModelAccessService? untypedModelAccessService;
    private readonly IMicroflowExpressionService? microflowExpressionService;
    private readonly MxVcsService? versionControlService;
    private Logger log = null!;
    private bool subscribed;
    private bool stateRestored;          // first-Open guard for cross-session restore
    private bool sessionsChangedHooked;  // first-Open guard for persistence hook
    private bool managerForwardingHooked; // first-Open guard for output/exit forwarding
    // Captured on UI-thread during Open(); SaveStateBestEffort runs from the
    // SessionsChanged event which may fire on a non-UI thread where
    // CurrentApp can return null.
    private string? cachedProjectDir;
    // Track the most recently opened view model so we can detach its
    // event subscriptions during ExtensionUnloading — before our IL gets
    // unloaded and Mendix's later DisposeWindow tries to invoke handlers
    // that no longer have valid method bodies.
    private TerminalPaneViewModel? activeViewModel;

    /// <summary>
    /// First-run notices queued by <see cref="TryFirstRunApply"/>, drained by
    /// the VM's "ready" handler via the consume-Func passed into its
    /// constructor. List rather than array because the VM consumes once and
    /// clears.
    /// </summary>
    private readonly List<string> pendingFirstRunNotices = new();
    private bool firstRunChecked;
    private bool upgradeChecked;

    private readonly IExtensionFileService extensionFileService;

    [ImportingConstructor]
    public TerminalPaneExtension(
        ILocalRunConfigurationsService localRunConfigs,
        IExtensionFileService extensionFileService,
        IPageGenerationService pageGenerationService,
        INavigationManagerService navigationManagerService,
        IMicroflowService microflowService,
        [Import(AllowDefault = true)] INameValidationService? nameValidationService = null,
        [Import(AllowDefault = true)] IUntypedModelAccessService? untypedModelAccessService = null,
        [Import(AllowDefault = true)] IMicroflowExpressionService? microflowExpressionService = null,
        [Import(AllowDefault = true)] MxVcsService? versionControlService = null)
    {
        this.localRunConfigs = localRunConfigs;
        this.extensionFileService = extensionFileService;
        this.pageGenerationService = pageGenerationService;
        this.navigationManagerService = navigationManagerService;
        this.microflowService = microflowService;
        this.nameValidationService = nameValidationService;
        this.untypedModelAccessService = untypedModelAccessService;
        this.microflowExpressionService = microflowExpressionService;
        this.versionControlService = versionControlService;
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
        EnsureManagerForwardingHooked();
        TryAutoStartActionServer();
        TryFirstRunApply();
        TryUpgradeApply();
        TryRepairInconsistentSettings();
        TryRestoreTabsOnFirstOpen();

        // Append ?theme=dark|light from Studio Pro's persisted preference
        // (SQLite probe) so the WebView gets the host's actual theme — NOT
        // the OS app theme that prefers-color-scheme would otherwise return.
        var indexUri = new Uri(WebServerBaseUrl, "index.html");
        var themeQuery = ResolveThemeQuery();
        if (themeQuery != null)
            indexUri = new Uri($"{indexUri}?theme={themeQuery}");
        log?.Info($"InitWebView indexUri={indexUri} theme-from-probe={themeQuery ?? "<none>"}");
        // Resolve once at Open(); the path is stable for the lifetime of the
        // extension (it's the deployed extensions/Concord/skills/ folder).
        var bundledSkillsRoot = extensionFileService.ResolvePath("skills");
        var bundledRulesRoot = extensionFileService.ResolvePath("rules");
        log?.Info($"[skills] bundled-root={bundledSkillsRoot}");
        log?.Info($"[rules] bundled-root={bundledRulesRoot}");

        var vm = new TerminalPaneViewModel(
            title: "Concord",
            manager: manager,
            getCurrentApp: () => CurrentApp,
            webIndexUri: indexUri,
            log: log!,
            getApplicationRootUrl: () =>
            {
                var model = CurrentApp;
                if (model is null) return null;
                try { return localRunConfigs.GetActiveConfiguration(model)?.ApplicationRootUrl; }
                catch (Exception ex) { log?.Warn($"GetActiveConfiguration threw: {ex.Message}"); return null; }
            },
            bundledSkillsRoot: bundledSkillsRoot,
            bundledRulesRoot: bundledRulesRoot,
            consumePendingFirstRunNotices: () =>
            {
                var notices = pendingFirstRunNotices.ToArray();
                pendingFirstRunNotices.Clear();
                return notices;
            });
        activeViewModel = vm;
        return vm;
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
            // Bulletproof unload — Mendix's host catches anything that escapes
            // and surfaces it as a popup/error. During ALC teardown the JIT can
            // throw BadImageFormatException when re-resolving any P/Invoke
            // whose native lib has already been unloaded; we don't want that to
            // reach the host. Independent try/catches so a failing log call
            // doesn't suppress disposal, and disposal failure doesn't leak.
            try { log?.Info("ExtensionUnloading — disposing all PTYs and action server"); } catch { /* ignore */ }
            // Null out the active view model so any in-flight Output/Exited
            // event from the manager (now being torn down) short-circuits in
            // the forwarder and never reaches into a half-disposed VM.
            try { activeViewModel?.DetachLifecycleHooks(); } catch { /* ignore */ }
            activeViewModel = null;
            try { manager.Dispose(); } catch { /* ignore — best-effort cleanup */ }
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

    /// <summary>
    /// Subscribe once to manager.Output and manager.Exited at the extension
    /// level — NOT in TerminalPaneViewModel. The view model used to do this
    /// itself and unsubscribe via OnClosed, but during Studio Pro shutdown
    /// Mendix's DockablePane DisposeWindow walks OnClosed subscribers and
    /// any delegate pointing into our (about-to-be-unloaded) IL surfaced
    /// BadImageFormatException popups. Owning the subscription here keeps
    /// the delegate target inside the extension (which lives across
    /// pane-open/close cycles); the view model just exposes
    /// PostOutput / PostExit and is treated as a passive forwarding target.
    /// </summary>
    private void EnsureManagerForwardingHooked()
    {
        if (managerForwardingHooked) return;
        managerForwardingHooked = true;
        manager.Output += ForwardOutput;
        manager.Exited += ForwardExit;
    }

    private void ForwardOutput(string tabId, byte[] bytes)
    {
        try { activeViewModel?.PostOutput(tabId, bytes); }
        catch (Exception ex) { log?.Warn($"[forward-output] {ex.GetType().Name}: {ex.Message}"); }
    }

    private void ForwardExit(string tabId, int? code)
    {
        try { activeViewModel?.PostExit(tabId, code); }
        catch (Exception ex) { log?.Warn($"[forward-exit] {ex.GetType().Name}: {ex.Message}"); }
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
        if (!settings.McpServerEnabled) return;

        // v4.2.0: hydrate the diagnostic-logging flag on the live logger
        // before any CDP traffic. The CdpClient constructed below captures
        // the same `log` reference, so this controls whether its DEBUG
        // lines actually persist.
        log.DiagnosticEnabled = settings.MaiaDiagnosticLogging;

        try
        {
            var ui = new StudioProUiAutomation(
                runHotkey: "F5",
                stopHotkey: "Shift+F5",
                refreshHotkey: settings.RefreshFromDiskHotkey,
                log: log);
            HostServices.SetUiAutomation(ui);
            var probe = new RunStateProbe(() =>
            {
                var model = CurrentApp;
                if (model is null) return null;
                try { return localRunConfigs.GetActiveConfiguration(model)?.ApplicationRootUrl; }
                catch (Exception ex) { log?.Warn($"GetActiveConfiguration threw: {ex.Message}"); return null; }
            });
            HostServices.SetRunStateProbe(probe);

            // Wire the App + RunConfigurations hosts with the pane's live
            // CurrentApp closure and the MEF-imported run-configs service.
            // The Host11xEntry registers placeholders at activation time
            // (because IModel and ILocalRunConfigurationsService are not
            // available until the pane opens); we swap in real instances
            // here before the action server begins dispatching tools, so
            // get_app_status / get_active_run_configuration are functional
            // from the first MCP request.
            HostServices.SetApp(new StudioProAppHost11x(() => CurrentApp));
            HostServices.SetRunConfigurations(new RunConfigurationsHost11x(() => CurrentApp, localRunConfigs));

            // Wire the 7 model-tier Interop hosts. IModel is per-project, so
            // this happens here (pane Open) rather than in Host11xEntry. Skip
            // when CurrentApp is unavailable — SPMCP tools will surface
            // NotInitialized until a project is open in Studio Pro, which is
            // the correct user-facing contract.
            var currentModel = CurrentApp;
            if (currentModel != null)
            {
                HostServices.SetModel(new ModelHost11x(currentModel));
                HostServices.SetDomainModel(new DomainModelHost11x(currentModel, nameValidationService));
                HostServices.SetPageGeneration(new PageGenerationHost11x(currentModel, pageGenerationService, navigationManagerService));
                HostServices.SetNavigation(new NavigationHost11x(currentModel, navigationManagerService));
                HostServices.SetVersionControl(new VersionControlHost11x(currentModel, versionControlService));
                HostServices.SetUntypedModel(new UntypedModelHost11x(currentModel, untypedModelAccessService));
                HostServices.SetMicroflowAuthoring(new MicroflowAuthoringHost11x(currentModel, microflowService, microflowExpressionService, untypedModelAccessService));
            }
            else
            {
                log.Warn("[concord-mcp] CurrentApp is null at pane Open — model-tier tools will throw NotInitialized until a project is open");
            }

            // Build Maia plumbing only on Windows when the toggle is on AND
            // when the Studio Pro version exposes a Maia panel (11.10+).
            // 11.6–11.9 don't have a Maia panel — Maia plumbing would create
            // a CDP client that has nothing to inject into.
            Terminal.Maia.CdpClient? sharedCdp = null;
            bool maiaSupported = StudioProThemeProbe.IsMaiaSupported(StudioProVersionFromExePath());
            bool maiaEnabled = OperatingSystem.IsWindows() && settings.MaiaIntegrationEnabled && maiaSupported;
            if (maiaEnabled)
            {
                // v4.2.0: singleton CdpClient — port discovery and the
                // WebSocket are reused across all Maia tool calls. The v4.1.x
                // pattern (`() => new CdpClient()` per call) spawned a fresh
                // PowerShell process and a fresh WebSocket on EVERY maia__*
                // call, which DoS'd Studio Pro's CDP under the cocktail-test
                // workload (45+ status/wait calls in 30 min). See
                // docs/superpowers/specs/2026-05-09-bridge-hardening-implementation.md.
                // v4.2.1: lifetime owned by the manager — disposed when the
                // action server is restarted (settings toggle off→on) so a
                // long-lived Studio Pro session doesn't accumulate orphan
                // ClientWebSockets per Maia toggle cycle.
                sharedCdp = new Terminal.Maia.CdpClient(log);
                var transports = new Terminal.Maia.IMaiaTransport[]
                {
                    new Terminal.Maia.CdpInjectedTransport(() => sharedCdp),
                    new Terminal.Maia.CdpChatTransport(() => sharedCdp),
                };
                var router = new Terminal.Maia.MaiaRouter(transports);
                _ = router.ProbeAllAsync(CancellationToken.None);  // fire-and-forget; safe
                var maia = new Terminal.Maia.MaiaActions(router);
                HostServices.SetMaiaActions(maia);
            }
            else
            {
                HostServices.SetMaiaActions(null);
            }

            // Gate catalog families from settings before starting the server so
            // tools/list reflects the current toggles immediately.
            Host11xEntry.Catalog?.SetFamilyEnabled(Terminal.Mcp.ToolFamily.UiActions, settings.StudioProActionsEnabled);
            Host11xEntry.Catalog?.SetFamilyEnabled(Terminal.Mcp.ToolFamily.Maia, maiaEnabled);

            // Fixed default — saved settings.McpServerPort is ignored.
            // The server falls back to a free OS-assigned port if 7783 is taken.
            manager.StartActionServer(
                StudioProActionServer.DefaultPort,
                log,
                cdpClient: sharedCdp);
            // Log the LIVE bound port — DefaultPort may have been busy and the
            // server quietly fell back to an OS-assigned free port.
            var boundPort = manager.CurrentActionServerPort ?? StudioProActionServer.DefaultPort;
            log.Info($"[concord-mcp] auto-started server on port {boundPort} (maia={maiaEnabled})");
        }
        catch (Exception ex)
        {
            log.Error("[concord-mcp] auto-start failed", ex);
        }
    }

    /// <summary>
    /// On a project that has never had Concord settings persisted, write the
    /// new (v4.1.0) defaults to disk: <c>.mcp.json</c> for Claude + Copilot,
    /// bundled skill folders into <c>.claude/skills</c> and
    /// <c>.github/skills</c>, plus the settings file itself. The Concord MCP
    /// bridge is started separately by <see cref="TryAutoStartActionServer"/>
    /// (which runs first, sees the new defaults, and fires automatically).
    /// Queues advisory notices for the VM to flush once the JS side is ready.
    /// </summary>
    private void TryFirstRunApply()
    {
        if (firstRunChecked) return;
        firstRunChecked = true;

        try
        {
            var dir = (CurrentApp?.Root as IProject)?.DirectoryPath;
            if (dir is null) return;

            var settingsPath = Path.Combine(dir, "resources", "terminal-settings.json");
            if (File.Exists(settingsPath))
            {
                log.Info("[first-run] settings file exists — skipping auto-apply");
                return;
            }

            log.Info("[first-run] no settings file — applying defaults to project");
            var defaults = TerminalSettings.Defaults();
            // Force McpEnabled = false on Studio Pro versions that don't expose
            // the mendix-studio-pro MCP (10.x, 11.6–11.9). Keeps the saved
            // settings file aligned with what's actually wired so the Settings
            // UI checkbox state matches reality.
            if (!ProbeStudioProMcpAvailable())
            {
                defaults = defaults with { McpEnabled = false };
                log.Info("[first-run] Studio Pro version doesn't expose mendix-studio-pro MCP; forcing McpEnabled=false in defaults");
            }
            // Same gate for Maia features: versions < 11.10 have no Maia panel.
            if (!StudioProThemeProbe.IsMaiaSupported(StudioProVersionFromExePath()))
            {
                defaults = defaults with
                {
                    MaiaIntegrationEnabled = false,
                    MaiaDiagnosticLogging = false,
                };
                log.Info("[first-run] Studio Pro version doesn't expose Maia panel; forcing Maia*=false in defaults");
            }

            // "Empty prev" so the apply chain treats every CLI as newly-added
            // and writes/installs accordingly.
            var emptyPrev = defaults with
            {
                McpEnabled = false,
                McpClients = Array.Empty<string>(),
                McpServerEnabled = false,
                SkillsEnabled = false,
                SkillClients = Array.Empty<string>(),
            };

            var bundledSkillsRoot = extensionFileService.ResolvePath("skills");
            var bundledRulesRoot = extensionFileService.ResolvePath("rules");

            var touched = SettingsApplyHelper.ApplyAll(
                dir,
                bundledSkillsRoot,
                bundledRulesRoot,
                emptyPrev,
                defaults,
                log,
                currentActionServerPort: () => manager.CurrentActionServerPort,
                probeStudioProMcpPort:      () => ProbeStudioProMcpPort(),
                probeStudioProMcpAvailable: () => ProbeStudioProMcpAvailable());

            // Stamp the version we just applied so TryUpgradeApply on the
            // SAME Open() call sees a current stamp and is a no-op
            // (otherwise it would re-run the same apply seconds later).
            (defaults with { LastAppliedVersion = ConcordVersion() }).Save(dir);

            pendingFirstRunNotices.AddRange(BuildFirstRunNotices(defaults, touched));
            log.Info($"[first-run] applied {touched.Length} target(s); queued {pendingFirstRunNotices.Count} notice(s)");
        }
        catch (Exception ex)
        {
            log.Error("[first-run] apply failed", ex);
        }
    }

    /// <summary>
    /// Detects "first open after Concord upgrade" by comparing the settings
    /// file's stored <see cref="TerminalSettings.LastAppliedVersion"/> to
    /// the current Concord assembly version. On mismatch, re-defaults the
    /// wiring keys (MCP + skills + sub-toggles) and runs the apply chain so
    /// a customer who upgrades from older Concord with off-by-default keys
    /// gets new functionality materialized to disk without needing to open
    /// Settings and Save manually. Runtime preferences (shell, theme, ring
    /// buffer, scrollback, restore-tabs, refresh hotkey, ports) are
    /// preserved verbatim from the loaded settings.
    /// Idempotent per upgrade: stamps <c>LastAppliedVersion</c> on save
    /// so subsequent Opens at the same Concord version are no-ops, and
    /// deliberately no-ops when the stamp is newer (a colleague pulled the
    /// project from a machine running a more recent Concord — we never
    /// downgrade their wiring).
    /// </summary>
    private void TryUpgradeApply()
    {
        if (upgradeChecked) return;
        upgradeChecked = true;

        try
        {
            var dir = (CurrentApp?.Root as IProject)?.DirectoryPath;
            if (dir is null) return;

            var settingsPath = Path.Combine(dir, "resources", "terminal-settings.json");
            if (!File.Exists(settingsPath))
            {
                // No file → first-run path already handled it (and stamped
                // the current version). Nothing to upgrade.
                return;
            }

            var loaded = TerminalSettings.Load(dir);
            var current = ConcordVersion();
            if (!IsUpgradeApplyNeeded(loaded.LastAppliedVersion, current))
            {
                log.Info($"[upgrade] stamp '{loaded.LastAppliedVersion ?? "<null>"}' satisfies current {current} — no-op");
                return;
            }

            log.Info($"[upgrade] applying wiring defaults: stamp '{loaded.LastAppliedVersion ?? "<null>"}' -> {current}");

            var defaults = TerminalSettings.Defaults();
            // Same gate as TryFirstRunApply: force McpEnabled = false on Studio
            // Pro versions without the mendix-studio-pro MCP. Critical for
            // cross-version migration: a project that lived on 11.10+ may now
            // open on 10.x, and we don't want the upgrade-apply to re-enable
            // an MCP the host can't serve.
            if (!ProbeStudioProMcpAvailable())
            {
                defaults = defaults with { McpEnabled = false };
                log.Info("[upgrade] Studio Pro version doesn't expose mendix-studio-pro MCP; forcing McpEnabled=false");
            }
            // Same gate for Maia features.
            if (!StudioProThemeProbe.IsMaiaSupported(StudioProVersionFromExePath()))
            {
                defaults = defaults with
                {
                    MaiaIntegrationEnabled = false,
                    MaiaDiagnosticLogging = false,
                };
                log.Info("[upgrade] Studio Pro version doesn't expose Maia panel; forcing Maia*=false");
            }
            // Re-default ONLY wiring keys (MCP + skills + sub-toggles).
            // Runtime prefs (shell/theme/ports/hotkeys/scrollback/etc.) come
            // through from `loaded` unchanged. Trade-off accepted: a user
            // who deliberately turned MCP off in an older Concord gets it
            // re-enabled once on first open after upgrade — the banner
            // points them to Settings if they want to re-disable.
            var nextSettings = loaded with
            {
                McpEnabled              = defaults.McpEnabled,
                McpClients              = defaults.McpClients,
                McpServerEnabled        = defaults.McpServerEnabled,
                StudioProActionsEnabled = defaults.StudioProActionsEnabled,
                MaiaIntegrationEnabled  = defaults.MaiaIntegrationEnabled,
                SkillsEnabled           = defaults.SkillsEnabled,
                SkillClients            = defaults.SkillClients,
                LastAppliedVersion      = current,
            };
            // If Maia isn't supported by this Studio Pro version, also force
            // diagnostic logging off — otherwise a project that lived on
            // 11.10+ with diagnostic on would silently carry that flag
            // forward to a 11.6 open where it can never fire.
            if (!StudioProThemeProbe.IsMaiaSupported(StudioProVersionFromExePath()))
            {
                nextSettings = nextSettings with { MaiaDiagnosticLogging = false };
            }

            // "Empty prev" so the apply chain treats every CLI as newly-
            // added — same pattern as TryFirstRunApply. The underlying
            // helpers (Upsert, InstallAll) are idempotent; re-installing a
            // skill folder that's already current is a safe overwrite.
            var emptyPrev = nextSettings with
            {
                McpEnabled = false,
                McpClients = Array.Empty<string>(),
                McpServerEnabled = false,
                SkillsEnabled = false,
                SkillClients = Array.Empty<string>(),
            };

            var bundledSkillsRoot = extensionFileService.ResolvePath("skills");
            var bundledRulesRoot = extensionFileService.ResolvePath("rules");

            var touched = SettingsApplyHelper.ApplyAll(
                dir,
                bundledSkillsRoot,
                bundledRulesRoot,
                emptyPrev,
                nextSettings,
                log,
                currentActionServerPort: () => manager.CurrentActionServerPort,
                probeStudioProMcpPort:      () => ProbeStudioProMcpPort(),
                probeStudioProMcpAvailable: () => ProbeStudioProMcpAvailable());

            nextSettings.Save(dir);

            var prevStamp = loaded.LastAppliedVersion ?? "pre-tracking";
            if (touched.Length > 0)
            {
                pendingFirstRunNotices.Add($"Updated to {current}. Rewired: {string.Join(", ", touched)}. Open Settings to adjust.");
                // Only re-surface SP-MCP / Maia advisories when an actual
                // wiring change happened. Cosmetic patch upgrades (e.g.
                // 4.1.0 → 4.1.1 with no apply diff) shouldn't re-show
                // reminders the user already acknowledged on prior runs.
                pendingFirstRunNotices.AddRange(BuildAdvisoryNotices(nextSettings));
            }
            else
            {
                pendingFirstRunNotices.Add($"Updated to {current}. No changes.");
            }
            log.Info($"[upgrade] applied {touched.Length} target(s); queued {pendingFirstRunNotices.Count} notice(s)");
        }
        catch (Exception ex)
        {
            log.Error("[upgrade] apply failed", ex);
        }
    }

    /// <summary>
    /// One-shot repair for settings inconsistencies caused by past UI bugs.
    /// Specifically the v5.0.0-alpha.x Bug-1 regression that cleared
    /// <c>McpClients</c> to empty while leaving <c>McpServerEnabled</c> true
    /// — the result was that <c>concord-mcp</c> got removed from <c>.mcp.json</c>
    /// and re-saves never re-wrote it because the apply path saw "MCP server on
    /// but zero clients" and skipped. This method detects that combination via
    /// <see cref="TerminalSettings.TryRepair"/>, saves the corrected settings,
    /// and forces a one-time re-apply so the missing entries reappear.
    /// </summary>
    private void TryRepairInconsistentSettings()
    {
        try
        {
            var dir = (CurrentApp?.Root as IProject)?.DirectoryPath;
            if (dir is null) return;

            var settingsPath = Path.Combine(dir, "resources", "terminal-settings.json");
            if (!File.Exists(settingsPath)) return;

            var loaded = TerminalSettings.Load(dir);
            var (repaired, changed) = TerminalSettings.TryRepair(loaded);
            if (!changed)
            {
                log.Info("[settings-repair] no inconsistencies detected");
                return;
            }

            log.Info($"[settings-repair] restored defaults for McpServerEnabled+empty-McpClients or SkillsEnabled+empty-SkillClients");
            repaired.Save(dir);

            // Re-apply so the missing .mcp.json / config.toml entries get
            // rewritten. Treat the broken loaded state as `prev` so the diff
            // sees the additions properly.
            var bundledSkillsRoot = extensionFileService.ResolvePath("skills");
            var bundledRulesRoot = extensionFileService.ResolvePath("rules");
            var touched = SettingsApplyHelper.ApplyAll(
                dir,
                bundledSkillsRoot,
                bundledRulesRoot,
                loaded,        // prev (the damaged state)
                repaired,      // next (the corrected state)
                log,
                currentActionServerPort: () => manager.CurrentActionServerPort,
                probeStudioProMcpPort:      () => ProbeStudioProMcpPort(),
                probeStudioProMcpAvailable: () => ProbeStudioProMcpAvailable());
            log.Info($"[settings-repair] re-applied {touched.Length} target(s) after repair");
        }
        catch (Exception ex)
        {
            log.Error("[settings-repair] failed", ex);
        }
    }

    /// <summary>
    /// Decide whether to run upgrade-apply for a given settings stamp + the
    /// current assembly version. Apply when stamp is missing/empty or
    /// strictly older (semver). Equal or newer is a no-op so a colleague
    /// who pulls a project last-edited from a machine running a newer
    /// Concord doesn't have their wiring downgraded by an older runtime.
    /// </summary>
    internal static bool IsUpgradeApplyNeeded(string? stamp, string current)
    {
        if (string.IsNullOrEmpty(stamp)) return true;
        if (Version.TryParse(stamp, out var prev) && Version.TryParse(current, out var curr))
            return prev < curr;
        // Unparsable stamp: be conservative, only apply on a strict mismatch.
        return !string.Equals(stamp, current, StringComparison.Ordinal);
    }

    /// <summary>
    /// Three-part Concord version string read from the assembly (e.g. "4.1.0").
    /// Falls back to "0.0.0" only if the assembly has no version metadata,
    /// which would force every Open() to treat the install as the oldest
    /// possible version — safe-but-noisy.
    /// </summary>
    private static string ConcordVersion()
    {
        var v = typeof(TerminalPaneExtension).Assembly.GetName().Version;
        return v?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// Build the 1–3 advisory strings shown to the user on first run. Leads
    /// with a "Concord wired up" summary of what was applied, followed by
    /// shared SP-MCP / Maia advisories from <see cref="BuildAdvisoryNotices"/>.
    /// </summary>
    private string[] BuildFirstRunNotices(TerminalSettings s, string[] touched)
    {
        var notices = new List<string>();
        if (touched.Length > 0)
            notices.Add($"Concord ready. Wired: {string.Join(", ", touched)}.");
        notices.AddRange(BuildAdvisoryNotices(s));
        return notices.ToArray();
    }

    /// <summary>
    /// Conditional SP-MCP-disabled and Maia-panel advisories shared by the
    /// first-run and upgrade flows. The leading "Concord wired up" /
    /// "Concord upgraded" line is built by the caller; this helper only
    /// emits the shared advice strings.
    /// </summary>
    private string[] BuildAdvisoryNotices(TerminalSettings s)
    {
        var notices = new List<string>();
        try
        {
            var version = StudioProVersionFromExePath();
            // Only probe / advise when the running Studio Pro version actually
            // ships the built-in MCP server (11.10+). On older 11.x or any 10.x
            // the EnableMcpServer key is absent from Settings.sqlite so the
            // probe would return Enabled=null and the advisory would point
            // users at a Maia preferences pane that doesn't exist on their
            // version.
            if (!string.IsNullOrEmpty(version) && StudioProThemeProbe.IsMcpServerSupported(version))
            {
                var info = StudioProThemeProbe.ReadMcpServer(version);
                if (info.Enabled != true)
                    notices.Add("Studio Pro MCP is off. Enable it in Edit → Preferences → Maia → MCP Server, then reopen Concord.");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"[advisory] SP-MCP probe failed: {ex.Message}");
        }

        if (OperatingSystem.IsWindows() && s.MaiaIntegrationEnabled)
            notices.Add("Keep the Maia panel open while Maia tools are in use.");

        // v4.2.1: Codex now defaults-on. Surface the user-global config
        // write so the user is informed, not surprised. The notice fires on
        // first-run AND on upgrade-apply when Codex first lights up; once
        // the user has acknowledged it (or unticked Codex in Settings),
        // subsequent stamps don't re-show.
        var codexEnabled = (s.McpClients?.Any(c => string.Equals(c, "codex", StringComparison.OrdinalIgnoreCase)) ?? false)
                        || (s.SkillClients?.Any(c => string.Equals(c, "codex", StringComparison.OrdinalIgnoreCase)) ?? false);
        if (codexEnabled)
            notices.Add("Codex MCP wires ~/.codex/config.toml (user-global, outside the project tree). Untick Codex in Settings if you'd rather not.");

        return notices.ToArray();
    }

    /// <summary>
    /// Probe Studio Pro's MCP server port from <c>Settings.sqlite</c>.
    /// Returns null on probe failure (caller falls back to the saved port).
    /// Lives at extension level so both <see cref="TryFirstRunApply"/> and
    /// the VM's save flow can use it.
    /// </summary>
    private int? ProbeStudioProMcpPort()
    {
        try
        {
            var version = StudioProVersionFromExePath();
            if (string.IsNullOrEmpty(version)) return null;
            var info = StudioProThemeProbe.ReadMcpServer(version);
            return info.Port;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// True iff the running Studio Pro version exposes the built-in
    /// <c>mendix-studio-pro</c> MCP server (requires 11.10+). Passed through
    /// to <see cref="SettingsApplyHelper.ApplyAll"/> so first-run / upgrade
    /// apply on an older Studio Pro skips wiring AND cleans up any stale
    /// <c>mendix-studio-pro</c> entry left behind from a prior 11.10+ open.
    /// </summary>
    private bool ProbeStudioProMcpAvailable()
    {
        try
        {
            return StudioProThemeProbe.IsMcpServerSupported(StudioProVersionFromExePath());
        }
        catch
        {
            return false;
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
            var version = StudioProVersionFromExePath();
            if (string.IsNullOrEmpty(version))
            {
                log?.Info("[theme-probe] version not detected from exe path");
                return null;
            }
            var result = StudioProThemeProbe.Read(version);
            log?.Info($"[theme-probe] sp-version={version} {result.Diagnostic}");
            return result.Theme is null ? null : StudioProThemeProbe.ToUrlValue(result.Theme.Value);
        }
        catch (Exception ex)
        {
            log?.Warn($"[theme-probe] outer exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extracts a "<c>major.minor.patch</c>" version from Studio Pro's process exe path.
    /// Works for both Windows (<c>...\Mendix\11.10.0\modeler\studiopro.exe</c>) and the
    /// Mac bundle layout (e.g. <c>/Applications/Mendix Studio Pro 11.10.0.app/...</c>).
    /// Returns null if the path doesn't contain a version triple.
    /// </summary>
    internal static string? StudioProVersionFromExePath()
    {
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(exePath, @"\d+\.\d+\.\d+");
        return match.Success ? match.Value : null;
    }
}
