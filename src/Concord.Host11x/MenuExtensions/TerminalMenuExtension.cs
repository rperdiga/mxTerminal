using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Terminal;
using Terminal.Interop;
using Concord.Host11x;
using Concord.Host11x.Pane;

namespace Concord.Host11x.MenuExtensions;

[Export(typeof(MenuExtension))]
public sealed class TerminalMenuExtension : MenuExtension
{
    [Import(typeof(Host11xEntry))]
#pragma warning disable CS0649  // Field is assigned by MEF, never read directly
    private Host11xEntry? _entry = null;
#pragma warning restore CS0649

    private readonly IDockingWindowService docking;

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
