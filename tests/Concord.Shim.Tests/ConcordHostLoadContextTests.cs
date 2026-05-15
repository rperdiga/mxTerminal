using System.Reflection;
using Concord.Shim.Tests.Fakes;
using FluentAssertions;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Xunit;

namespace Concord.Shim.Tests;

public class ConcordHostLoadContextTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "Concord.Shim.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Load_FakeHostDll_AssemblyLoadsIntoCustomContext()
    {
        var dllPath = FakeHostBuilder.EmitFakeHost(_tempDir);
        using var ctx = new ConcordHostLoadContext(_tempDir);

        var asm = ctx.LoadFromAssemblyPath(dllPath);

        asm.Should().NotBeNull();
        asm.GetName().Name.Should().Be("FakeHost");
    }

    [Fact]
    public void LoadedHostType_DockablePaneExtensionBaseType_IsReferenceEqualToDefaultContextType()
    {
        // This is the load-bearing invariant. The fake host's
        // FakeDockablePane subclasses DockablePaneExtension. If the
        // Resolving event correctly redirects Mendix.StudioPro.ExtensionsAPI
        // to the default context, then the loaded type's BaseType MUST
        // be ReferenceEqual to typeof(DockablePaneExtension) from this
        // test assembly's perspective.
        var dllPath = FakeHostBuilder.EmitFakeHost(_tempDir);
        using var ctx = new ConcordHostLoadContext(_tempDir);
        var asm = ctx.LoadFromAssemblyPath(dllPath);

        var fakeType = asm.GetType("FakeHost.FakeDockablePane")!;

        fakeType.BaseType.Should().BeSameAs(typeof(DockablePaneExtension));
    }

    [Fact]
    public void LoadedInstance_CastsToDefaultContextType_AndVirtualDispatchWorks()
    {
        var dllPath = FakeHostBuilder.EmitFakeHost(_tempDir);
        using var ctx = new ConcordHostLoadContext(_tempDir);
        var asm = ctx.LoadFromAssemblyPath(dllPath);
        var fakeType = asm.GetType("FakeHost.FakeDockablePane")!;

        var instance = Activator.CreateInstance(fakeType)!;
        var castInstance = instance as DockablePaneExtension;

        castInstance.Should().NotBeNull();
        castInstance!.Id.Should().Be("FakeHost"); // virtual property dispatch
    }

    [Fact]
    public void ExactlyOne_ExtensionsApi_LoadedInAppDomain_AfterCustomContextLoads()
    {
        var dllPath = FakeHostBuilder.EmitFakeHost(_tempDir);
        using var ctx = new ConcordHostLoadContext(_tempDir);
        ctx.LoadFromAssemblyPath(dllPath);

        var apiCopies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name == "Mendix.StudioPro.ExtensionsAPI")
            .ToList();

        apiCopies.Should().HaveCount(1,
            "Resolving event must redirect ExtensionsAPI requests from the custom " +
            "context to the default context — never load a second copy.");
    }
}
