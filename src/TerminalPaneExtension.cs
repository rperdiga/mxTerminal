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
    public const string ID = "Terminal";
    public override string Id => ID;

    private readonly TerminalSessionManager manager;
    private readonly ILocalRunConfigurationsService localRunConfigs;
    private Logger log = null!;
    private bool subscribed;

    [ImportingConstructor]
    public TerminalPaneExtension(ILocalRunConfigurationsService localRunConfigs)
    {
        this.localRunConfigs = localRunConfigs;
        manager = new TerminalSessionManager(new PtyNetFactory());
    }

    public override DockablePaneViewModelBase Open()
    {
        EnsureLogger();
        EnsureLifecycleSubscribed();
        TryAutoStartActionServer();

        var indexUri = new Uri(WebServerBaseUrl, "index.html");
        return new TerminalPaneViewModel(
            title: "Terminal",
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
            var actions = new StudioProActions(probe, ui);
            manager.StartActionServer(settings.ActionsServerPort, actions, log);
            log.Info($"[actions] auto-started server on port {settings.ActionsServerPort}");
        }
        catch (Exception ex)
        {
            log.Error("[actions] auto-start failed", ex);
        }
    }
}
