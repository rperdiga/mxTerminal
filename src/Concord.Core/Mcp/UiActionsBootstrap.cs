namespace Terminal.Mcp;

using System.Text.Json.Nodes;

/// <summary>
/// Registers all 6 UI-action tools (run_app, stop_app, save_all,
/// refresh_project, get_active_run_configuration, get_app_status) into the
/// ToolCatalog under <see cref="ToolFamily.UiActions"/> at MEF activation time.
/// Visibility is controlled by the catalog's SetFamilyEnabled gate; the
/// hardcoded dispatch in StudioProActionServer has been removed.
/// </summary>
public static class UiActionsBootstrap
{
    public static void Register(ToolCatalog catalog)
    {
        var actions = new StudioProActions();

        Register(catalog, "run_app",                      async _ => (object)await actions.RunAppAsync());
        Register(catalog, "stop_app",                     async _ => (object)await actions.StopAppAsync());
        Register(catalog, "save_all",                     async _ => (object)await actions.SaveAllAsync());
        Register(catalog, "refresh_project",              async _ => (object)await actions.RefreshProjectAsync());
        Register(catalog, "get_active_run_configuration", async _ => (object)await actions.GetActiveRunConfigurationAsync());
        Register(catalog, "get_app_status",               async _ => (object)await actions.GetAppStatusAsync());
    }

    private static void Register(ToolCatalog catalog, string name, Func<JsonObject, Task<object>> invoke)
        => catalog.Register(new RegisteredTool(name, ToolFamily.UiActions, invoke));

    private sealed record RegisteredTool(string Name, ToolFamily Family, Func<JsonObject, Task<object>> Invoke) : ITool;
}
