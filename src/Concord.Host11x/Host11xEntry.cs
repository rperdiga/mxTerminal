namespace Concord.Host11x;

using System.ComponentModel.Composition;
using Terminal;
using Terminal.Interop;
using Terminal.Mcp;

/// <summary>
/// Single point of MEF activation for the 11.x host. Wires HostContext +
/// HostServices on first use; other MEF exports in this assembly take a
/// dependency on Host11xEntry via [Import] to guarantee it runs first.
/// </summary>
[Export(typeof(Host11xEntry))]
public class Host11xEntry
{
    private static int _initialized;

    public static ToolCatalog? Catalog { get; private set; }

    [ImportingConstructor]
    public Host11xEntry()
    {
        if (System.Threading.Interlocked.Exchange(ref _initialized, 1) != 0) return;

        HostContext.Initialize(TargetMode.Studio11x);
        // App + RunConfigurations are registered as placeholders here because
        // they need IModel (CurrentApp) and the MEF-imported
        // ILocalRunConfigurationsService respectively — neither is available
        // at MEF activation time. The pane swaps in fully-wired instances via
        // HostServices.SetApp / SetRunConfigurations in TryAutoStartActionServer
        // before the action server begins dispatching tools.
        HostServices.Register(
            app: new Interop.StudioProAppHost11x(() => null),
            runConfigs: new Interop.RunConfigurationsHost11x(() => null, service: null),
            runState: new Interop.RunStateHost11x(),
            moduleImport: new Interop.ModuleImportHost11x());

        var catalog = new ToolCatalog(TargetMode.Studio11x);
        Spmcp.SpmcpToolBootstrap11x.Register(catalog);
        Terminal.Mcp.UiActionsBootstrap.Register(catalog);
        // Maia panel ships with Studio Pro 11.10+. Host11x covers 11.6–11.x,
        // so we have to runtime-check the version: 11.6–11.9 don't have a
        // Maia panel and registering maia__* would surface dead tools.
        // Probe the version at activation time from the exe path so the
        // catalog is correct for the lifetime of this host instance.
        var spVersion = StudioProThemeProbe.StudioProVersionFromExePath();
        if (StudioProThemeProbe.IsMaiaSupported(spVersion))
        {
            Terminal.Mcp.MaiaToolsBootstrap.Register(catalog);
        }
        Catalog = catalog;
        ToolCatalogRegistry.Active = catalog;
    }
}
