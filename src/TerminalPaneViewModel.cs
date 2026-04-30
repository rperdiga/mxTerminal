using Eto.Forms;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;
using MxStudioProTerminal.Messages;
using System.Text.Json;

namespace MxStudioProTerminal;

public sealed class TerminalPaneViewModel : WebViewDockablePaneViewModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TerminalSessionManager manager;
    private readonly Func<IModel?> getCurrentApp;
    private readonly Uri webIndexUri;
    private readonly Logger log;

    private IWebView? webView;
    private Action<string, byte[]>? outputHandler;
    private Action<string, int?>?  exitedHandler;

    public TerminalPaneViewModel(
        string title,
        TerminalSessionManager manager,
        Func<IModel?> getCurrentApp,
        Uri webIndexUri,
        Logger log)
    {
        Title = title;
        this.manager = manager;
        this.getCurrentApp = getCurrentApp;
        this.webIndexUri = webIndexUri;
        this.log = log;
    }

    public override void InitWebView(IWebView webView)
    {
        this.webView = webView;
        webView.MessageReceived += OnWebViewMessage;
        webView.Address = webIndexUri;

        outputHandler = (tabId, bytes) => Post("output", new OutputPayload(tabId, Convert.ToBase64String(bytes)));
        exitedHandler = (tabId, code) => Post("exit", new ExitPayload(tabId, code));
        manager.Output += outputHandler;
        manager.Exited += exitedHandler;

        OnClosed += () =>
        {
            if (outputHandler != null) manager.Output -= outputHandler;
            if (exitedHandler != null) manager.Exited -= exitedHandler;
            outputHandler = null;
            exitedHandler = null;
        };
    }

    private void OnWebViewMessage(object? sender, MessageReceivedEventArgs e)
    {
        try
        {
            switch (e.Message)
            {
                case "ready":
                case "listTabs":
                    Post("tabsList", new TabsListPayload(
                        manager.ListSessions().Select(s => new SessionInfoPayload(s.TabId, s.Title, s.ShellPath, s.Cwd, s.Alive)).ToList()
                    ));
                    break;

                case "createTab":
                    HandleCreateTab(GetData<CreateTabPayload>(e));
                    break;

                case "closeTab":
                {
                    var p = GetData<CloseTabPayload>(e);
                    manager.Close(p.TabId);
                    Post("tabClosed", new TabClosedPayload(p.TabId));
                    break;
                }

                case "input":
                {
                    var p = GetData<InputPayload>(e);
                    manager.Write(p.TabId, Convert.FromBase64String(p.DataB64));
                    break;
                }

                case "resize":
                {
                    var p = GetData<ResizePayload>(e);
                    manager.Resize(p.TabId, p.Cols, p.Rows);
                    break;
                }

                case "replay":
                {
                    var p = GetData<ReplayPayload>(e);
                    var snap = manager.SnapshotBuffer(p.TabId);
                    Post("replayData", new ReplayDataPayload(p.TabId, Convert.ToBase64String(snap)));
                    break;
                }

                case "openSettings":
                {
                    var dir = GetProjectDir();
                    var s = dir != null ? TerminalSettings.Load(dir) : TerminalSettings.Defaults();
                    Post("settings", BuildSettingsPayload(s));
                    break;
                }

                case "saveSettings":
                {
                    var p = GetData<SaveSettingsPayload>(e);
                    var dir = GetProjectDir();
                    if (dir == null) { Post("error", new ErrorPayload("No Mendix app is open")); break; }
                    var current = TerminalSettings.Load(dir);
                    var updated = current with
                    {
                        ShellPath = p.ShellPath,
                        Args = p.Args,
                        RingBufferKB = p.RingBufferKB ?? current.RingBufferKB,
                        XtermScrollbackLines = p.XtermScrollbackLines ?? current.XtermScrollbackLines,
                        Theme = p.Theme ?? current.Theme,
                    };
                    updated.Save(dir);
                    Post("settings", BuildSettingsPayload(updated));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"OnWebViewMessage({e.Message}) failed", ex);
            Post("error", new ErrorPayload(ex.Message, e.Message));
        }
    }

    private async void HandleCreateTab(CreateTabPayload p)
    {
        try
        {
            var dir = GetProjectDir() ?? Environment.CurrentDirectory;
            var settings = TerminalSettings.Load(GetProjectDir() ?? "");
            var shell = p.ShellPath ?? settings.ShellPath;
            var args = p.Args ?? settings.Args;
            var cwd = p.Cwd ?? dir;

            var tabId = await manager.CreateSessionAsync(shell, args, cwd, p.Cols, p.Rows);
            Post("tabCreated", new TabCreatedPayload(tabId, Path.GetFileNameWithoutExtension(shell), shell, cwd));
        }
        catch (Exception ex)
        {
            log.Error("CreateTab failed", ex);
            Application.Instance.Invoke(() => Post("error", new ErrorPayload($"Failed to start shell: {ex.Message}", "createTab")));
        }
    }

    private void Post(string message, object data)
    {
        if (webView == null) return;
        try
        {
            // Mendix's webView.PostMessage(message, data) emits the second arg's
            // property names verbatim — no camelCase conversion. Our DTO records
            // are PascalCase, but the JS side expects camelCase. Pre-serialize
            // through System.Text.Json with web defaults so the JsonNode that
            // Mendix forwards is already camelCase.
            var node = JsonSerializer.SerializeToNode(data, Json);
            Application.Instance.Invoke(() => webView.PostMessage(message, node!));
        }
        catch (Exception ex) { log.Error($"PostMessage({message}) failed", ex); }
    }

    private static T GetData<T>(MessageReceivedEventArgs e) where T : class
    {
        if (e.Data is null) throw new InvalidOperationException("Missing data");
        return e.Data.Deserialize<T>(Json)
            ?? throw new InvalidOperationException($"Bad payload for {typeof(T).Name}");
    }

    private string? GetProjectDir() => (getCurrentApp()?.Root as IProject)?.DirectoryPath;

    private static SettingsPayload BuildSettingsPayload(TerminalSettings s) => new(
        ShellPath: s.ShellPath,
        Args: s.Args,
        RingBufferKB: s.RingBufferKB,
        XtermScrollbackLines: s.XtermScrollbackLines,
        Theme: s.Theme,
        AvailableShells: ShellDetector.Detect()
            .Select(o => new ShellOptionPayload(o.Name, o.Path))
            .ToList());
}
