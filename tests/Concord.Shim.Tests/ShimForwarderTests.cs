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
