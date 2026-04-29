using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;

namespace MxStudioProTerminal;

[Export(typeof(MenuExtension))]
public sealed class TerminalMenuExtension : MenuExtension
{
    private readonly IDockingWindowService docking;

    [ImportingConstructor]
    public TerminalMenuExtension(IDockingWindowService docking) => this.docking = docking;

    public override IEnumerable<MenuViewModel> GetMenus()
    {
        yield return new MenuViewModel(
            caption: "Terminal",
            action: () => docking.OpenPane(TerminalPaneExtension.ID));
    }
}
