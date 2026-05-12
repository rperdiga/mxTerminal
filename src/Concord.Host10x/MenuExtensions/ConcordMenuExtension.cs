namespace Concord.Host10x.MenuExtensions;

using System.ComponentModel.Composition;
using Eto.Forms;
using Mendix.StudioPro.ExtensionsAPI.UI.Menu;

/// <summary>
/// Minimal MEF export so Studio Pro 10.24.13 recognizes Concord as a loaded
/// extension. Triggers Host10xEntry activation (initializes HostContext +
/// HostServices). Clicking the menu item shows an honest "10.x preview"
/// message — full functionality (terminal pane, MCP server, Maia bridge)
/// requires porting Host11x's MEF-exported classes against the 10.x API
/// surface, which lands in W2 once SPMCP source-merges into Concord and we
/// reconcile the 10.21.1 vs 11.6.2 ExtensionsAPI deltas.
/// </summary>
[Export(typeof(MenuExtension))]
public class ConcordMenuExtension : MenuExtension
{
    [Import(typeof(Host10xEntry))]
#pragma warning disable CS0414  // Sentinel: field is read by MEF activation, never used by host code
    private Host10xEntry? _entry = null;
#pragma warning restore CS0414

    public override IEnumerable<MenuViewModel> GetMenus()
    {
        yield return new MenuViewModel(
            caption: "Concord (10.x preview)",
            action: ShowPreviewMessage);
    }

    private static void ShowPreviewMessage()
    {
        MessageBox.Show(
            "Concord 5.0.0-alpha.1 — Studio Pro 10.24.13 preview.\n\n" +
            "The extension is loaded and the cross-version foundation is in place. " +
            "Full functionality (terminal pane, MCP server, Maia bridge) is coming " +
            "in a follow-up release as the Host11x MEF surface is ported against " +
            "the 10.x ExtensionsAPI.\n\n" +
            "For full functionality today, use Studio Pro 11.10 or later.",
            "Concord",
            MessageBoxButtons.OK,
            MessageBoxType.Information);
    }
}
