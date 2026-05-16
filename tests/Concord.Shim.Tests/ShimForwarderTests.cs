using Concord.Shim.Menu;
using Concord.Shim.Pane;
using Concord.Shim.Tests.Fakes;
using Concord.Shim.WebServer;
using FluentAssertions;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.WebServer;
using NSubstitute;
using Xunit;

namespace Concord.Shim.Tests;

[Collection("HostKickstart")]
public class TerminalPaneExtensionShimTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "Concord.Shim.Tests", Guid.NewGuid().ToString("N"));

    public TerminalPaneExtensionShimTests()
    {
        HostKickstart.ResetForTesting();
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        HostKickstart.ResetForTesting();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Ctor_CapturesServices_PassesPositionallyToHostInstance()
    {
        var fakeRunConfigs = Substitute.For<ILocalRunConfigurationsService>();
        var fakeFileService = Substitute.For<IExtensionFileService>();
        var sideChannel = Path.Combine(_tempDir, "entry.log");

        FakeHostBuilder.EmitFakeHostWithPaneAndEntry(_tempDir, sideChannel);

        HostKickstart.OverrideForTesting(_tempDir, "FakeHost", "FakeHost.FakeHostEntry");

        var shim = new TerminalPaneExtensionShim(
            localRunConfigs: fakeRunConfigs,
            extensionFileService: fakeFileService,
            pageGenerationService: Substitute.For<IPageGenerationService>(),
            navigationManagerService: Substitute.For<INavigationManagerService>(),
            microflowService: Substitute.For<IMicroflowService>());

        // Override the host type name the shim looks up
        // (production: "Concord.Host{N}x.Pane.TerminalPaneExtension").
        shim.TestOverrideInnerTypeName("FakeHost.FakePane");

        var inner = shim.EnsureInnerInstance();

        var markerProp = inner.GetType().GetProperty("LocalRunConfigsMarker")!;
        markerProp.GetValue(inner).Should().Be(fakeRunConfigs.GetType().Name);
    }

    // Regression for the 2026-05-15 phase-5-smoke "ResolvePath KeyNotFound"
    // bug. SP's ExtensionFileService.ResolvePath uses GetCallingAssembly()
    // to look up the extension folder; only Concord.Shim is in SP's
    // dictionary. The shim must wrap the imported IExtensionFileService in
    // a ShimExtensionFileService before handing it to the inner host so
    // the inner's ResolvePath calls re-dispatch through Concord.Shim's
    // assembly.
    [Fact]
    public void Ctor_WrapsExtensionFileService_InnerReceivesShimExtensionFileService()
    {
        var sideChannel = Path.Combine(_tempDir, "entry.log");
        FakeHostBuilder.EmitFakeHostWithPaneAndEntry(_tempDir, sideChannel);
        HostKickstart.OverrideForTesting(_tempDir, "FakeHost", "FakeHost.FakeHostEntry");

        var shim = new TerminalPaneExtensionShim(
            localRunConfigs: Substitute.For<ILocalRunConfigurationsService>(),
            extensionFileService: Substitute.For<IExtensionFileService>(),
            pageGenerationService: Substitute.For<IPageGenerationService>(),
            navigationManagerService: Substitute.For<INavigationManagerService>(),
            microflowService: Substitute.For<IMicroflowService>());
        shim.TestOverrideInnerTypeName("FakeHost.FakePane");

        var inner = shim.EnsureInnerInstance();
        var fileServiceMarker = (string)inner.GetType().GetProperty("FileServiceMarker")!.GetValue(inner)!;

        fileServiceMarker.Should().Be("ShimExtensionFileService",
            "the inner host must receive the wrapper, not SP's raw IExtensionFileService — " +
            "otherwise ResolvePath's GetCallingAssembly() returns the inner host's assembly, " +
            "which isn't in SP's extension dictionary, and the lookup KeyNotFoundExceptions.");
    }

    // Regression for the 2026-05-15 phase-5-smoke "UIExtensionBase unset on
    // inner" bug. The inner host's Open() reads `this.WebServerBaseUrl` and
    // `this.CurrentApp` (UIExtensionBase members SP only populates on the
    // shim, not on Activator.CreateInstance'd instances). The shim must call
    // the inner's __ConcordShim_SetUIContext seam BEFORE inner.Open() so the
    // inner's shadow accessors resolve to the shim's SP-populated values.
    [Fact]
    public void Open_ForwardsUIContextToInner_BeforeDelegating()
    {
        FakeHostBuilder.EmitFakeHostWithPaneSeamAndEntry(_tempDir);
        HostKickstart.OverrideForTesting(_tempDir, "FakeHost", "FakeHost.FakeHostEntry");

        var shim = new TerminalPaneExtensionShim(
            localRunConfigs: Substitute.For<ILocalRunConfigurationsService>(),
            extensionFileService: Substitute.For<IExtensionFileService>(),
            pageGenerationService: Substitute.For<IPageGenerationService>(),
            navigationManagerService: Substitute.For<INavigationManagerService>(),
            microflowService: Substitute.For<IMicroflowService>());
        shim.TestOverrideInnerTypeName("FakeHost.FakePaneWithSeam");

        // FakePaneWithSeam.Open() returns null and never invokes the captured
        // getters, so the shim's lambdas (which read its own unset
        // UIExtensionBase) never fire. The seam's setter runs synchronously
        // before inner.Open() returns.
        _ = shim.Open();

        var inner = shim.EnsureInnerInstance();
        var wasSet = inner.GetType().GetProperty("UIContextWasSet")!.GetValue(inner);
        wasSet.Should().Be(true,
            "the shim must invoke __ConcordShim_SetUIContext before delegating Open() — " +
            "otherwise the inner's CurrentApp / WebServerBaseUrl getters resolve to " +
            "unpopulated UIExtensionBase fields and throw at first access.");
    }
}

[Collection("HostKickstart")]
public class ConcordMenuExtensionShimTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "Concord.Shim.Tests", Guid.NewGuid().ToString("N"));

    public ConcordMenuExtensionShimTests()
    {
        HostKickstart.ResetForTesting();
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        HostKickstart.ResetForTesting();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Ctor_CapturesDocking_GetMenusDelegatesToInner()
    {
        var fakeDocking = Substitute.For<IDockingWindowService>();
        var sideChannel = Path.Combine(_tempDir, "entry.log");

        FakeHostBuilder.EmitFakeHostWithMenuAndEntry(_tempDir, sideChannel);
        HostKickstart.OverrideForTesting(_tempDir, "FakeHost", "FakeHost.FakeHostEntry");

        var shim = new ConcordMenuExtensionShim(fakeDocking);
        shim.TestOverrideInnerTypeName("FakeHost.FakeMenu");

        var menus = shim.GetMenus().ToList();

        menus.Should().HaveCount(1);
        menus[0].Caption.Should().Be("fake-caption");
    }
}

[Collection("HostKickstart")]
public class TerminalWebServerShimTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "Concord.Shim.Tests", Guid.NewGuid().ToString("N"));

    public TerminalWebServerShimTests()
    {
        HostKickstart.ResetForTesting();
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        HostKickstart.ResetForTesting();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void InitializeWebServer_DelegatesToInner()
    {
        var fakeFileService = Substitute.For<IExtensionFileService>();
        var fakeWebServer = Substitute.For<IWebServer>();
        var sideChannel = Path.Combine(_tempDir, "entry.log");

        FakeHostBuilder.EmitFakeHostWithWebServerAndEntry(_tempDir, sideChannel);
        HostKickstart.OverrideForTesting(_tempDir, "FakeHost", "FakeHost.FakeHostEntry");

        var shim = new TerminalWebServerShim(fakeFileService);
        shim.TestOverrideInnerTypeName("FakeHost.FakeWebServer");

        shim.InitializeWebServer(fakeWebServer);

        fakeWebServer.Received(1).AddRoute("fake", Arg.Any<HandleWebRequestAsync>());
    }
}
