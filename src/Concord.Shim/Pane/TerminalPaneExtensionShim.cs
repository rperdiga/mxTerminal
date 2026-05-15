using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using MxVcsService = Mendix.StudioPro.ExtensionsAPI.UI.Services.IVersionControlService;

namespace Concord.Shim.Pane;

/// <summary>
/// Shim forwarder for the host's TerminalPaneExtension. Mirrors the inner
/// type's [ImportingConstructor] shape exactly so Studio Pro's MEF binds
/// our imports the same way it would have bound the host's. Captures the
/// services and passes them positionally to the inner host type via
/// HostKickstart.CreateHostInstance.
///
/// Inner instantiation is lazy — defers until the first override that
/// needs it (Open) — so the load-context boundary cross is not paid at
/// MEF activation time.
///
/// Per OQ4 in the implementation plan: the inner host type's own
/// [Export]/[ImportingConstructor] attributes remain in place but are
/// vestigial under the shim. The shim drives instantiation; MEF inside
/// the inner load context never runs.
/// </summary>
[Export(typeof(DockablePaneExtension))]
public sealed class TerminalPaneExtensionShim : DockablePaneExtension
{
    // ID must match the inner pane's ID — Studio Pro binds the dockable
    // pane registration to this string. Both production hosts use "Concord".
    public override string Id => "Concord";

    private readonly object?[] _capturedServices;
    private string _innerTypeNameOverride = ""; // testing seam

    [ImportingConstructor]
    public TerminalPaneExtensionShim(
        ILocalRunConfigurationsService localRunConfigs,
        IExtensionFileService extensionFileService,
        IPageGenerationService pageGenerationService,
        INavigationManagerService navigationManagerService,
        IMicroflowService microflowService,
        [Import(AllowDefault = true)] INameValidationService? nameValidationService = null,
        [Import(AllowDefault = true)] IUntypedModelAccessService? untypedModelAccessService = null,
        [Import(AllowDefault = true)] IMicroflowExpressionService? microflowExpressionService = null,
        [Import(AllowDefault = true)] MxVcsService? versionControlService = null)
    {
        // Static cctor in this same class ensures HostKickstart fires before
        // any instance member is touched. (Per Phase 0 finding §"Probe bugs"
        // item A — DO NOT use [ModuleInitializer]; unreliable on .NET 10.)
        _capturedServices = new object?[]
        {
            localRunConfigs,
            extensionFileService,
            pageGenerationService,
            navigationManagerService,
            microflowService,
            nameValidationService,
            untypedModelAccessService,
            microflowExpressionService,
            versionControlService,
        };
    }

    static TerminalPaneExtensionShim()
    {
        // Triggers load-context creation + Host{N}xEntry static init on
        // first activation. Idempotent thereafter.
        try { HostKickstart.EnsureLoaded(); }
        catch (Exception ex)
        {
            ShimLog.Error("HostKickstart.EnsureLoaded threw during pane shim cctor", ex);
            throw;
        }
    }

    private DockablePaneExtension? _inner;

    internal DockablePaneExtension EnsureInnerInstance()
    {
        if (_inner is not null) return _inner;
        var typeName = string.IsNullOrEmpty(_innerTypeNameOverride)
            ? ResolveInnerTypeName()
            : _innerTypeNameOverride;
        var instance = ShimLog.Timed("PaneShim.CreateInner",
            () => HostKickstart.CreateHostInstance(typeName, _capturedServices));
        _inner = (DockablePaneExtension)instance;
        return _inner;
    }

    internal void TestOverrideInnerTypeName(string name) => _innerTypeNameOverride = name;

    private static string ResolveInnerTypeName()
        => $"{HostKickstart.LoadedHostAssemblyName}.Pane.TerminalPaneExtension";

    public override DockablePaneViewModelBase Open() => EnsureInnerInstance().Open();
}
