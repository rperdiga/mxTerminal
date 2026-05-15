using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Terminal;
using Terminal.Interop;
using Concord.Host11x;
using Concord.Host11x.Pane;

namespace Concord.Host11x.MenuExtensions;

// [shim-vestigial] Studio Pro's MEF sees only Concord.Shim.dll under the
// runtime-shim architecture (Phase 0 spike — 2026-05-15). The attributes
// below remain so the host can still be built and tested in isolation, but
// at production runtime Concord.Shim's *Shim forwarders drive instantiation
// via reflection — these attributes are dead metadata. See
// docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md
// §OQ4.
[Export(typeof(MenuExtension))]
public sealed class TerminalMenuExtension : MenuExtension
{
    [Import(typeof(Host11xEntry))]
#pragma warning disable CS0414  // Sentinel: field is read by MEF activation, never used by host code
    private Host11xEntry? _entry = null;
#pragma warning restore CS0414

    private readonly IDockingWindowService docking;

    // [shim-vestigial] Studio Pro's MEF sees only Concord.Shim.dll under the
    // runtime-shim architecture (Phase 0 spike — 2026-05-15). The attributes
    // below remain so the host can still be built and tested in isolation, but
    // at production runtime Concord.Shim's *Shim forwarders drive instantiation
    // via reflection — these attributes are dead metadata. See
    // docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md
    // §OQ4.
    [ImportingConstructor]
    public TerminalMenuExtension(IDockingWindowService docking) => this.docking = docking;

    public override IEnumerable<MenuViewModel> GetMenus()
    {
        // Caption is "Open Pane" rather than "Terminal" so the breadcrumb
        // reads "Extensions > Terminal > Open Pane" instead of stuttering
        // "Terminal > Terminal". Future siblings (Show Settings, Restart
        // Action Server, About...) slot in cleanly.
        yield return new MenuViewModel(
            caption: "Open Pane",
            action: () => docking.OpenPane(TerminalPaneExtension.ID));
    }
}
