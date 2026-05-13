namespace Concord.Host10x;

using System.ComponentModel.Composition;
using Terminal;
using Terminal.Interop;
using Terminal.Mcp;

/// <summary>
/// Single point of MEF activation for the 10.x host. Wires HostContext +
/// HostServices on first use. Other MEF exports in this assembly should
/// take a dependency on Host10xEntry via [Import] to guarantee it runs
/// first (mirrors Host11xEntry pattern).
///
/// W1 scope: Host10x only contributes this entry + 4 Interop stubs.
/// MEF-exported menu/pane/web-server classes are deferred until the
/// Task 1 spike confirms the 10.x ExtensionsAPI surface.
/// </summary>
[Export(typeof(Host10xEntry))]
public class Host10xEntry
{
    private static int _initialized;

    public static ToolCatalog? Catalog { get; private set; }

    [ImportingConstructor]
    public Host10xEntry()
    {
        if (System.Threading.Interlocked.Exchange(ref _initialized, 1) != 0) return;

        HostContext.Initialize(TargetMode.Studio10x);
        HostServices.Register(
            app: new Interop.StudioProAppHost10x(),
            runConfigs: new Interop.RunConfigurationsHost10x(),
            runState: new Interop.RunStateHost10x(),
            moduleImport: new Interop.ModuleImportHost10x());

        var catalog = new ToolCatalog(TargetMode.Studio10x);
        Spmcp.SpmcpToolBootstrap10x.Register(catalog);
        Catalog = catalog;
        ToolCatalogRegistry.Active = catalog;
    }
}
