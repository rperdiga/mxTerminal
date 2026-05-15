using System.Collections.Generic;
using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;

namespace Concord.Shim.Menu;

/// <summary>
/// Shim forwarder for the host's TerminalMenuExtension. Mirrors the inner
/// type's [ImportingConstructor] shape exactly (1 service:
/// IDockingWindowService) and forwards GetMenus to the inner instance.
/// </summary>
[Export(typeof(MenuExtension))]
public sealed class ConcordMenuExtensionShim : MenuExtension
{
    private readonly IDockingWindowService _docking;
    private string _innerTypeNameOverride = "";
    private MenuExtension? _inner;

    [ImportingConstructor]
    public ConcordMenuExtensionShim(IDockingWindowService docking)
        => _docking = docking;

    static ConcordMenuExtensionShim()
    {
        try { HostKickstart.EnsureLoaded(); }
        catch (Exception ex)
        {
            ShimLog.Error("HostKickstart.EnsureLoaded threw during menu shim cctor", ex);
            throw;
        }
    }

    internal void TestOverrideInnerTypeName(string name) => _innerTypeNameOverride = name;

    private MenuExtension EnsureInner()
    {
        if (_inner is not null) return _inner;
        var typeName = string.IsNullOrEmpty(_innerTypeNameOverride)
            ? ResolveInnerTypeName()
            : _innerTypeNameOverride;
        _inner = (MenuExtension)HostKickstart.CreateHostInstance(typeName, _docking);
        return _inner;
    }

    private static string ResolveInnerTypeName()
    {
        var asmName = HostKickstart.ResolveHostType("Concord.Host10x.Host10xEntry") is not null
            ? "Concord.Host10x" : "Concord.Host11x";
        return $"{asmName}.MenuExtensions.TerminalMenuExtension";
    }

    public override IEnumerable<MenuViewModel> GetMenus() => EnsureInner().GetMenus();
}
