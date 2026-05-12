namespace Concord.Host11x;

using System.ComponentModel.Composition;
using Terminal;
using Terminal.Interop;

/// <summary>
/// Single point of MEF activation for the 11.x host. Wires HostContext +
/// HostServices on first use; other MEF exports in this assembly take a
/// dependency on Host11xEntry via [Import] to guarantee it runs first.
/// </summary>
[Export(typeof(Host11xEntry))]
public class Host11xEntry
{
    private static int _initialized;

    [ImportingConstructor]
    public Host11xEntry()
    {
        if (System.Threading.Interlocked.Exchange(ref _initialized, 1) != 0) return;

        HostContext.Initialize(TargetMode.Studio11x);
        HostServices.Register(
            app: new Interop.StudioProAppHost11x(),
            runConfigs: new Interop.RunConfigurationsHost11x(),
            runState: new Interop.RunStateHost11x(),
            moduleImport: new Interop.ModuleImportHost11x());
    }
}
