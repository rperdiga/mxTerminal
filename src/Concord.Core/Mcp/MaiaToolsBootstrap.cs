namespace Terminal.Mcp;

using System.Text.Json.Nodes;
using Terminal;
using Terminal.Interop;

/// <summary>
/// Registers all 10 Maia tools (maia__send, maia__status, maia__wait,
/// maia__ask, maia__reset, maia__busy, maia__ping, maia__health,
/// maia__new_chat, maia__force_tier) into the ToolCatalog under
/// <see cref="ToolFamily.Maia"/> at MEF activation time.
/// <para>
/// MaiaActions stays pane-scoped (no singleton). Each delegate reads
/// <see cref="HostServices.MaiaActions"/> at invoke time (late binding) so
/// settings-save hot-swaps are visible without re-registration. When Maia
/// is disabled or not yet initialized the delegate returns an
/// <see cref="ActionResult.Fail"/> rather than throwing.
/// </para>
/// </summary>
public static class MaiaToolsBootstrap
{
    public static void Register(ToolCatalog catalog)
    {
        Register(catalog, "maia__send", async args =>
        {
            var maia = HostServices.MaiaActions;
            if (maia is null) return (object)ActionResult.Fail("Maia integration not enabled");
            return (object)await maia.SendAsync(
                args["prompt"]?.GetValue<string>() ?? "",
                args["sentinel"]?.GetValue<string>(),
                CancellationToken.None);
        });

        Register(catalog, "maia__status", async args =>
        {
            var maia = HostServices.MaiaActions;
            if (maia is null) return (object)ActionResult.Fail("Maia integration not enabled");
            return (object)await maia.StatusAsync(
                args["handle"]?.GetValue<string>() ?? "",
                CancellationToken.None);
        });

        Register(catalog, "maia__wait", async args =>
        {
            var maia = HostServices.MaiaActions;
            if (maia is null) return (object)ActionResult.Fail("Maia integration not enabled");
            return (object)await maia.WaitAsync(
                args["handle"]?.GetValue<string>() ?? "",
                args["timeout_sec"]?.GetValue<double>() ?? 60.0,
                CancellationToken.None);
        });

        Register(catalog, "maia__ask", async args =>
        {
            var maia = HostServices.MaiaActions;
            if (maia is null) return (object)ActionResult.Fail("Maia integration not enabled");
            return (object)await maia.AskAsync(
                args["prompt"]?.GetValue<string>() ?? "",
                args["timeout_sec"]?.GetValue<double>() ?? 60.0,
                CancellationToken.None);
        });

        Register(catalog, "maia__reset", async args =>
        {
            var maia = HostServices.MaiaActions;
            if (maia is null) return (object)ActionResult.Fail("Maia integration not enabled");
            return (object)await maia.ResetAsync(CancellationToken.None);
        });

        Register(catalog, "maia__busy", async args =>
        {
            var maia = HostServices.MaiaActions;
            if (maia is null) return (object)ActionResult.Fail("Maia integration not enabled");
            return (object)await maia.BusyAsync(CancellationToken.None);
        });

        Register(catalog, "maia__ping", async args =>
        {
            var maia = HostServices.MaiaActions;
            if (maia is null) return (object)ActionResult.Fail("Maia integration not enabled");
            return (object)await maia.PingAsync(
                args["timeout_sec"]?.GetValue<double>() ?? 5.0,
                CancellationToken.None);
        });

        Register(catalog, "maia__health", async args =>
        {
            var maia = HostServices.MaiaActions;
            if (maia is null) return (object)ActionResult.Fail("Maia integration not enabled");
            return (object)await maia.HealthAsync(CancellationToken.None);
        });

        Register(catalog, "maia__new_chat", async args =>
        {
            var maia = HostServices.MaiaActions;
            if (maia is null) return (object)ActionResult.Fail("Maia integration not enabled");
            return (object)await maia.NewChatAsync(CancellationToken.None);
        });

        Register(catalog, "maia__force_tier", async args =>
        {
            var maia = HostServices.MaiaActions;
            if (maia is null) return (object)ActionResult.Fail("Maia integration not enabled");
            return (object)await maia.ForceTierAsync(
                args["name"]?.GetValue<string>() ?? "",
                CancellationToken.None);
        });
    }

    private static void Register(ToolCatalog catalog, string name, Func<JsonObject, Task<object>> invoke)
        => catalog.Register(new RegisteredTool(name, ToolFamily.Maia, invoke));

    private sealed record RegisteredTool(string Name, ToolFamily Family, Func<JsonObject, Task<object>> Invoke) : ITool;
}
