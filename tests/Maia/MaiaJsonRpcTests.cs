using FluentAssertions;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Terminal;
using Terminal.Interop;
using Terminal.Maia;
using Terminal.Mcp;
using Xunit;

namespace Terminal.Tests.Maia;

/// <summary>
/// Shares the "ActionServer" collection with StudioProActionServerTests so both
/// run sequentially and don't race on ToolCatalogRegistry.Active + HostServices.
/// </summary>
[Collection("ActionServer")]
public class MaiaJsonRpcTests : IAsyncLifetime
{
    private sealed class FakeProbe : IRunStateProbe
    {
        public string? GetActiveUrl() => null;
        public int? GetActivePort() => null;
        public Task<RunState> IsRunningAsync(CancellationToken ct = default) => Task.FromResult(RunState.Stopped);
    }
    private sealed class FakeUi : IStudioProUiAutomation
    {
        public bool TriggerRun() => true;
        public bool TriggerStop() => true;
        public bool TriggerRefreshFromDisk() => true;
        public bool TriggerSaveAll() => true;
        public string? LastFailureReason => null;
    }
    private sealed class StubTransport : IMaiaTransport
    {
        public string Name => "stub";
        public int Tier => 1;
        public Task<HealthStatus> HealthCheckAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, 1, Name, 0));
        public Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
            => Task.FromResult(new SendResult(sentinel, sentinel, Name, DateTimeOffset.UtcNow));
        public Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
            => Task.FromResult(new StatusResult(true, "ok", false, 0, Name));
        public Task ResetAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private StudioProActionServer? server;
    private HttpClient http = null!;

    /// <summary>
    /// Build a server with the given gate values. Caller owns the lifetime of both
    /// returned objects (the IAsyncLifetime fixture binds the field-backed pair;
    /// per-test gating tests construct + dispose their own pair).
    /// </summary>
    private static async Task<(StudioProActionServer server, HttpClient http)> BuildServerAsync(
        bool studioProActionsEnabled, bool maiaIntegrationEnabled)
    {
        HostServices.Reset();
        HostServices.SetRunStateProbe(new FakeProbe());
        HostServices.SetUiAutomation(new FakeUi());

        // Wire the real Maia instance into HostServices when maiaIntegrationEnabled.
        if (maiaIntegrationEnabled)
        {
            var router = new MaiaRouter(new IMaiaTransport[] { new StubTransport() });
            await router.ProbeAllAsync(CancellationToken.None);
            var maia = new MaiaActions(router);
            HostServices.SetMaiaActions(maia);
        }
        else
        {
            HostServices.SetMaiaActions(null);
        }

        // Build the catalog with both UI-action and Maia tool families, then
        // gate visibility via SetFamilyEnabled to match the requested flags.
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        UiActionsBootstrap.Register(catalog);
        MaiaToolsBootstrap.Register(catalog);
        catalog.SetFamilyEnabled(ToolFamily.UiActions, studioProActionsEnabled);
        catalog.SetFamilyEnabled(ToolFamily.Maia, maiaIntegrationEnabled);
        ToolCatalogRegistry.Active = catalog;

        var s = new StudioProActionServer(port: 0);
        s.Start();
        var c = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{s.Port}") };
        return (s, c);
    }

    public async Task InitializeAsync()
    {
        (server, http) = await BuildServerAsync(
            studioProActionsEnabled: true, maiaIntegrationEnabled: true);
    }

    public Task DisposeAsync()
    {
        server?.Dispose();
        http.Dispose();
        HostServices.Reset();
        ToolCatalogRegistry.Active = null;
        return Task.CompletedTask;
    }

    private async Task<JsonDocument> Post(string body)
    {
        using var resp = await http.PostAsync("/mcp", new StringContent(body, Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ToolsList_IncludesMaiaToolsWhenEnabled()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        var names = doc.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        names.Should().Contain(new[] {
            "maia__send", "maia__status", "maia__wait",
            "maia__ask", "maia__reset", "maia__force_tier"
        });
    }

    [Fact]
    public async Task ToolsCall_MaiaSend_ReturnsHandle()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"maia__send","arguments":{"prompt":"hi"}}}""");
        var text = doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        var inner = JsonDocument.Parse(text).RootElement;
        inner.GetProperty("status").GetString().Should().Be("sent");
    }

    [Fact]
    public async Task ToolsList_StudioProActionsDisabled_ReturnsOnlyMaiaTools()
    {
        var savedCatalog = ToolCatalogRegistry.Active;
        var savedMaia = HostServices.MaiaActions;
        var (s, c) = await BuildServerAsync(
            studioProActionsEnabled: false, maiaIntegrationEnabled: true);
        try
        {
            using var resp = await c.PostAsync("/mcp",
                new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""",
                    Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

            var names = doc.RootElement.GetProperty("result").GetProperty("tools")
                .EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();

            names.Should().BeEquivalentTo(new[] {
                "maia__send", "maia__status", "maia__wait",
                "maia__ask",  "maia__reset",  "maia__force_tier",
                // v4.2.1 introspection tools.
                "maia__busy", "maia__ping", "maia__health", "maia__new_chat",
            });
            // Belt-and-braces: confirm none of the studio-pro tools leaked in.
            names.Should().NotContain("run_app");
            names.Should().NotContain("stop_app");
            names.Should().NotContain("refresh_project");
            names.Should().NotContain("save_all");
            names.Should().NotContain("get_active_run_configuration");
            names.Should().NotContain("get_app_status");
        }
        finally
        {
            s.Dispose();
            c.Dispose();
            // Restore fixture-level state so subsequent tests use the right catalog.
            ToolCatalogRegistry.Active = savedCatalog;
            HostServices.SetMaiaActions(savedMaia);
        }
    }

    [Fact]
    public async Task ToolsList_MaiaIntegrationDisabled_ReturnsOnlyStudioProTools()
    {
        var savedCatalog = ToolCatalogRegistry.Active;
        var savedMaia = HostServices.MaiaActions;
        var (s, c) = await BuildServerAsync(
            studioProActionsEnabled: true, maiaIntegrationEnabled: false);
        try
        {
            using var resp = await c.PostAsync("/mcp",
                new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""",
                    Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

            var names = doc.RootElement.GetProperty("result").GetProperty("tools")
                .EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();

            names.Should().BeEquivalentTo(new[] {
                "run_app", "stop_app", "refresh_project",
                "save_all", "get_active_run_configuration", "get_app_status",
            });
            // Belt-and-braces: confirm no maia__* tools leaked in.
            names.Should().NotContain("maia__send");
            names.Should().NotContain("maia__status");
            names.Should().NotContain("maia__wait");
            names.Should().NotContain("maia__ask");
            names.Should().NotContain("maia__reset");
            names.Should().NotContain("maia__force_tier");
        }
        finally
        {
            s.Dispose();
            c.Dispose();
            // Restore fixture-level state so subsequent tests use the right catalog.
            ToolCatalogRegistry.Active = savedCatalog;
            HostServices.SetMaiaActions(savedMaia);
        }
    }
}
