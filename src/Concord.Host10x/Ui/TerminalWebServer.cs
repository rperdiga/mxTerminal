using System.ComponentModel.Composition;
using System.Net;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.WebServer;
using Terminal;
using Terminal.Interop;
using Concord.Host10x;

namespace Concord.Host10x.Ui;

[Export(typeof(WebServerExtension))]
public sealed class TerminalWebServer : WebServerExtension
{
    [Import(typeof(Host10xEntry))]
#pragma warning disable CS0414  // Field is assigned by MEF, never read directly
    private Host10xEntry? _entry = null;
#pragma warning restore CS0414

    private readonly IExtensionFileService extensionFileService;

    [ImportingConstructor]
    public TerminalWebServer(IExtensionFileService extensionFileService)
    {
        this.extensionFileService = extensionFileService;
    }

    public override void InitializeWebServer(IWebServer webServer)
    {
        webServer.AddRoute("index.html", ServeIndex);
        webServer.AddRoute("terminal.bundle.js", ServeBundle);
        webServer.AddRoute("terminal.bundle.js.map", ServeBundleMap);
    }

    private async Task ServeIndex(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
    {
        var path = extensionFileService.ResolvePath("wwwroot", "index.html");
        await Serve(response, path, "text/html", ct);
    }

    private async Task ServeBundle(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
    {
        var path = extensionFileService.ResolvePath("wwwroot", "terminal.bundle.js");
        await Serve(response, path, "text/javascript", ct);
    }

    private async Task ServeBundleMap(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
    {
        var path = extensionFileService.ResolvePath("wwwroot", "terminal.bundle.js.map");
        await Serve(response, path, "application/json", ct);
    }

    private async Task Serve(HttpListenerResponse response, string path, string contentType, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }

        var fileContents = await File.ReadAllBytesAsync(path, ct);
        response.ContentType = contentType;
        response.ContentLength64 = fileContents.Length;
        response.StatusCode = 200;

        await response.OutputStream.WriteAsync(fileContents, ct);
        response.Close();
    }
}
