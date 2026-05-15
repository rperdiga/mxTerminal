using Concord.Shim.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Concord.Shim.Tests;

// Sequential, NOT parallel — HostKickstart has process-wide static state.
[CollectionDefinition("HostKickstart", DisableParallelization = true)]
public class HostKickstartCollection { }

[Collection("HostKickstart")]
public class HostKickstartTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "Concord.Shim.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _sideChannel;

    public HostKickstartTests()
    {
        Directory.CreateDirectory(_tempDir);
        _sideChannel = Path.Combine(_tempDir, "fake-entry.log");
        // CRITICAL: tests must NOT clobber each other's static state.
        HostKickstart.ResetForTesting();
    }

    public void Dispose()
    {
        HostKickstart.ResetForTesting();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void EnsureLoaded_FiresFakeHostEntryExactlyOnce_OnMultipleCalls()
    {
        var dllPath = FakeHostBuilder.EmitFakeHostWithEntry(_tempDir, _sideChannel);

        HostKickstart.OverrideForTesting(
            hostFolder: _tempDir,
            hostAssemblyName: "FakeHost",
            entryTypeName: "FakeHost.FakeHostEntry");

        HostKickstart.EnsureLoaded();
        HostKickstart.EnsureLoaded();
        HostKickstart.EnsureLoaded();

        File.ReadAllText(_sideChannel).Should().Be("fake-entry-ran\n");
    }

    [Fact]
    public void EnsureLoaded_MissingHostFolder_ThrowsClearMessage()
    {
        HostKickstart.OverrideForTesting(
            hostFolder: Path.Combine(_tempDir, "nope"),
            hostAssemblyName: "FakeHost",
            entryTypeName: "FakeHost.FakeHostEntry");

        var act = () => HostKickstart.EnsureLoaded();

        act.Should().Throw<DirectoryNotFoundException>()
           .WithMessage("*nope*");
    }

    [Fact]
    public void EnsureLoaded_AfterPriorFailure_DoesNotRetry_RethrowsWrappedException()
    {
        HostKickstart.OverrideForTesting(
            hostFolder: Path.Combine(_tempDir, "missing-on-purpose"),
            hostAssemblyName: "FakeHost",
            entryTypeName: "FakeHost.FakeHostEntry");

        // First call: original DirectoryNotFoundException.
        var firstAttempt = () => HostKickstart.EnsureLoaded();
        firstAttempt.Should().Throw<DirectoryNotFoundException>();

        // Second call: wrapped InvalidOperationException; inner is the
        // original exception. Does NOT re-execute the load logic.
        var secondAttempt = () => HostKickstart.EnsureLoaded();
        secondAttempt.Should().Throw<InvalidOperationException>()
            .WithMessage("*previously failed*")
            .WithInnerException<DirectoryNotFoundException>();
    }

    [Fact]
    public void ResolveHostType_ReturnsTypeFromCustomLoadContext_NotDefaultContext()
    {
        var dllPath = FakeHostBuilder.EmitFakeHostWithEntry(_tempDir, _sideChannel);
        HostKickstart.OverrideForTesting(_tempDir, "FakeHost", "FakeHost.FakeHostEntry");
        HostKickstart.EnsureLoaded();

        var paneType = HostKickstart.ResolveHostType("FakeHost.FakeDockablePane");

        paneType.Should().NotBeNull();
        paneType!.Assembly.GetName().Name.Should().Be("FakeHost");
        // Different load contexts mean different (logical) assemblies even
        // if the file is the same.
        System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(paneType.Assembly)
            .Should().NotBeSameAs(System.Runtime.Loader.AssemblyLoadContext.Default);
    }
}
