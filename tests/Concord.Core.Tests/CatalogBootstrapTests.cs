namespace Concord.Core.Tests;

using System.Text.Json.Nodes;
using FluentAssertions;
using Terminal;
using Terminal.Interop;
using Terminal.Maia;
using Terminal.Mcp;
using Xunit;

public class CatalogBootstrapTests : IDisposable
{
    private sealed class FakeProbe : IRunStateProbe
    {
        public string? GetActiveUrl() => null;
        public int? GetActivePort() => null;
        public System.Threading.Tasks.Task<RunState> IsRunningAsync(
            System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(RunState.Stopped);
    }
    private sealed class FakeUi : IStudioProUiAutomation
    {
        public bool TriggerRun() => false;
        public bool TriggerStop() => false;
        public bool TriggerRefreshFromDisk() => false;
        public bool TriggerSaveAll() => false;
        public string? LastFailureReason => null;
    }
    private sealed class StubTransport : IMaiaTransport
    {
        public string Name => "stub";
        public int Tier => 1;
        public System.Threading.Tasks.Task<HealthStatus> HealthCheckAsync(System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult(new HealthStatus(true, 1, Name, 0));
        public System.Threading.Tasks.Task<SendResult> SendAsync(string prompt, string sentinel, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult(new SendResult(sentinel, sentinel, Name, System.DateTimeOffset.UtcNow));
        public System.Threading.Tasks.Task<StatusResult> StatusAsync(string handle, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult(new StatusResult(true, "ok", false, 0, Name));
        public System.Threading.Tasks.Task ResetAsync(System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    public CatalogBootstrapTests()
    {
        HostServices.Reset();
        HostServices.SetRunStateProbe(new FakeProbe());
        HostServices.SetUiAutomation(new FakeUi());
    }

    public void Dispose()
    {
        HostServices.Reset();
        ToolCatalogRegistry.Active = null;
    }

    [Fact]
    public void UiActionsBootstrap_RegistersAll6Tools()
    {
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        UiActionsBootstrap.Register(catalog);

        var names = catalog.ListVisibleNames();
        names.Should().BeEquivalentTo(new[]
        {
            "run_app", "stop_app", "save_all", "refresh_project",
            "get_active_run_configuration", "get_app_status",
        });

        // Verify all are in UiActions family
        var tools = catalog.ListVisibleTools();
        tools.Should().AllSatisfy(t => t.Family.Should().Be(ToolFamily.UiActions));
    }

    [Fact]
    public void MaiaToolsBootstrap_RegistersAll10Tools()
    {
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        MaiaToolsBootstrap.Register(catalog);

        var names = catalog.ListVisibleNames();
        names.Should().BeEquivalentTo(new[]
        {
            "maia__send", "maia__status", "maia__wait", "maia__ask", "maia__reset",
            "maia__busy", "maia__ping", "maia__health", "maia__new_chat", "maia__force_tier",
        });

        // Verify all are in Maia family
        var tools = catalog.ListVisibleTools();
        tools.Should().AllSatisfy(t => t.Family.Should().Be(ToolFamily.Maia));
    }

    [Fact]
    public async Task MaiaToolsBootstrap_WithNoMaiaInstance_ReturnsFailure()
    {
        // HostServices.MaiaActions is null (Reset() was called in ctor)
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        MaiaToolsBootstrap.Register(catalog);
        ToolCatalogRegistry.Active = catalog;

        var result = await catalog.InvokeAsync("maia__ping", new JsonObject());
        var ar = result as ActionResult;
        ar.Should().NotBeNull();
        ar!.Error.Should().Be("Maia integration not enabled");
    }

    [Fact]
    public async Task MaiaToolsBootstrap_WithMaiaInstance_DelegatesToBridge()
    {
        // Set up a real MaiaActions backed by a stub transport
        var router = new MaiaRouter(new IMaiaTransport[] { new StubTransport() });
        await router.ProbeAllAsync(System.Threading.CancellationToken.None);
        var maia = new MaiaActions(router);
        HostServices.SetMaiaActions(maia);

        var catalog = new ToolCatalog(TargetMode.Studio10x);
        MaiaToolsBootstrap.Register(catalog);
        ToolCatalogRegistry.Active = catalog;

        // maia__ping should reach the stub transport (not return "not enabled")
        var result = await catalog.InvokeAsync("maia__ping", new JsonObject { ["timeout_sec"] = 2.0 });
        var ar = result as ActionResult;
        ar.Should().NotBeNull();
        // The stub transport reports alive, so ping should succeed
        ar!.Error.Should().BeNull();
    }

    [Fact]
    public void UiActionsBootstrap_FamilyDisabled_RemovesToolsFromVisibleList()
    {
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        UiActionsBootstrap.Register(catalog);
        catalog.SetFamilyEnabled(ToolFamily.UiActions, false);

        catalog.ListVisibleNames().Should().BeEmpty();
    }

    [Fact]
    public void MaiaToolsBootstrap_FamilyDisabled_RemovesToolsFromVisibleList()
    {
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        MaiaToolsBootstrap.Register(catalog);
        catalog.SetFamilyEnabled(ToolFamily.Maia, false);

        catalog.ListVisibleNames().Should().BeEmpty();
    }
}
