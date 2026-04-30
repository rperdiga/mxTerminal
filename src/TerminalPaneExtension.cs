using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.Events;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;

namespace Terminal;

[Export(typeof(DockablePaneExtension))]
public sealed class TerminalPaneExtension : DockablePaneExtension
{
    public const string ID = "Terminal";
    public override string Id => ID;

    private readonly TerminalSessionManager manager;
    private Logger log = null!;
    private bool subscribed;

    [ImportingConstructor]
    public TerminalPaneExtension()
    {
        // Singleton manager — created once, lives for app lifetime
        manager = new TerminalSessionManager(new PtyNetFactory());
    }

    public override DockablePaneViewModelBase Open()
    {
        EnsureLogger();
        EnsureLifecycleSubscribed();

        var indexUri = new Uri(WebServerBaseUrl, "index.html");
        return new TerminalPaneViewModel(
            title: "Terminal",
            manager: manager,
            getCurrentApp: () => CurrentApp,
            webIndexUri: indexUri,
            log: log,
            getApplicationRootUrl: () => null);   // wired up in Task 13
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
            try { log.Info("ExtensionUnloading — disposing all PTYs"); manager.DisposeAll(); }
            catch (Exception ex) { log.Error("DisposeAll on unload failed", ex); }
        });
    }
}
