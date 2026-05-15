using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.WebServer;

namespace Concord.Shim.WebServer;

/// <summary>
/// Shim forwarder for the host's TerminalWebServer. Mirrors the inner
/// type's [ImportingConstructor] shape exactly (1 service:
/// IExtensionFileService) and forwards InitializeWebServer to the inner
/// instance.
/// </summary>
[Export(typeof(WebServerExtension))]
public sealed class TerminalWebServerShim : WebServerExtension
{
    private readonly IExtensionFileService _fileService;
    private string _innerTypeNameOverride = "";
    private WebServerExtension? _inner;

    [ImportingConstructor]
    public TerminalWebServerShim(IExtensionFileService fileService)
        => _fileService = fileService;

    static TerminalWebServerShim()
    {
        try { HostKickstart.EnsureLoaded(); }
        catch (Exception ex)
        {
            ShimLog.Error("HostKickstart.EnsureLoaded threw during webserver shim cctor", ex);
            throw;
        }
    }

    internal void TestOverrideInnerTypeName(string name) => _innerTypeNameOverride = name;

    private WebServerExtension EnsureInner()
    {
        if (_inner is not null) return _inner;
        var typeName = string.IsNullOrEmpty(_innerTypeNameOverride)
            ? ResolveInnerTypeName()
            : _innerTypeNameOverride;
        _inner = (WebServerExtension)HostKickstart.CreateHostInstance(typeName, _fileService);
        return _inner;
    }

    private static string ResolveInnerTypeName()
        => $"{HostKickstart.LoadedHostAssemblyName}.Ui.TerminalWebServer";

    public override void InitializeWebServer(IWebServer webServer) => EnsureInner().InitializeWebServer(webServer);
}
