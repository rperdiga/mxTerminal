using System.Reflection;
using System.Runtime.Loader;
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

    // This asserts an AppDomain-wide invariant, not just this test's effect —
    // it passes because the test project holds exactly one PackageReference
    // to ExtensionsAPI and ConcordHostLoadContext.OnResolving prevents a
    // second copy. If a future test in this assembly triggers a second load
    // before this Fact runs, the assertion would fail in a confusing way.
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

    // Regression for the 2026-05-15 phase-5-smoke bug: an over-broad
    // "Microsoft.*" share rule deferred Microsoft.Extensions.Logging.Abstractions
    // to default ALC. SP 11.x doesn't have that assembly loaded — the host
    // ships its own copy — so deferral failed with FileNotFoundException
    // mid-way through Host11xEntry's ctor (SpmcpToolBootstrap11x.Register).
    // The fix: dynamic IsAlreadyLoadedInDefault check; anything SP didn't
    // load resolves locally from the host folder if a copy is present.
    [Fact]
    public void OnResolving_AssemblyNotInDefault_AndPresentLocally_LoadsLocally()
    {
        // Pick a name NOT in this test process's default ALC so the resolver
        // has to make the local-first choice.
        const string asmName = "Microsoft.Concord.Tests.HostOnlyDep";
        FakeHostBuilder.EmitFakeHost(_tempDir, assemblyName: asmName);

        using var ctx = new ConcordHostLoadContext(_tempDir);
        var asm = ctx.LoadFromAssemblyName(new AssemblyName(asmName));

        asm.Should().NotBeNull();
        asm.GetName().Name.Should().Be(asmName);
        // Loaded into our context, not default.
        AssemblyLoadContext.GetLoadContext(asm).Should().BeSameAs(ctx);
    }

    // Regression for the 2026-05-15 phase-5-smoke "Eto.Application.Instance
    // is null" bug. When the inner host loaded its own Eto.dll from bin-11x,
    // its Eto's Application.Instance static was null (a fresh static field
    // in a separate-context copy), and every PostMessage to the WebView
    // failed with NullReferenceException at Application.Instance.Invoke(...).
    // The fix: defer to default for any assembly SP already has loaded
    // (Eto is SP's UI toolkit; SP initializes Application.Instance early
    // in its startup). Even when the same-name file exists in the host
    // folder, the load context returns null so the CLR uses default's copy.
    [Fact]
    public void OnResolving_AssemblyLoadedInDefault_DefersToDefault_EvenWhenLocalCopyPresent()
    {
        // FluentAssertions is referenced by this test project, so it's
        // already loaded in default ALC. Plant a same-named file in the
        // host folder; the resolver MUST still defer to default.
        var fakeFluentAssertionsPath = Path.Combine(_tempDir, "FluentAssertions.dll");
        FakeHostBuilder.EmitFakeHost(_tempDir, assemblyName: "FluentAssertions");

        using var ctx = new ConcordHostLoadContext(_tempDir);
        var asm = ctx.LoadFromAssemblyName(new AssemblyName("FluentAssertions"));

        asm.Should().NotBeNull();
        // The CRITICAL assertion: the assembly we got is the one from default
        // ALC (real FluentAssertions used by xUnit/the test framework),
        // NOT the fake from our host folder.
        AssemblyLoadContext.GetLoadContext(asm).Should().BeSameAs(AssemblyLoadContext.Default,
            "an assembly SP/default-context already has loaded MUST be deferred — " +
            "otherwise statics like Eto.Application.Instance end up as fresh nulls in " +
            "a separate-context copy, breaking any code that depends on the shared singleton.");
    }

    // The Concord.* exemption (load locally even when default has a copy)
    // is verified empirically in production via shim.log lines like
    // "Resolved Concord.Core from <hostFolder>/Concord.Core.dll into <ctx>"
    // — they fire because the host DLL is loaded via LoadFromAssemblyPath
    // and its dependencies are resolved through THIS context's OnResolving
    // first. A unit test using LoadFromAssemblyName by name short-circuits
    // to default ALC before OnResolving fires (since default already has
    // Concord.Core loaded), so it can't model the production path. The
    // exemption is preserved in OnResolving's source for clarity and
    // future-proofing against changes in default-load order.

    // Regression: AssemblyDependencyResolver was removed in commit
    // "fix(shim): remove AssemblyDependencyResolver from ConcordHostLoadContext".
    // Its constructor invokes native corehost_resolve_component_dependencies
    // which fails on macOS Studio Pro (hostpolicy not initialized via
    // corehost_main). Don't re-introduce — if a future change needs
    // deps.json-driven resolution, find a different mechanism that works
    // on both platforms.
    [Fact]
    public void ConcordHostLoadContext_HasNoAssemblyDependencyResolverField()
    {
        var resolverType = typeof(System.Runtime.Loader.AssemblyDependencyResolver);
        var fields = typeof(ConcordHostLoadContext)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        fields.Should().NotContain(
            f => f.FieldType == resolverType,
            because: "AssemblyDependencyResolver's constructor calls native " +
                     "corehost_resolve_component_dependencies which fails on macOS " +
                     "Studio Pro — see fix commit message for full root cause.");
    }

    [Fact]
    public void TryResolveNativePath_FindsFileInMatchingRidNativeFolder()
    {
        // Mirrors production layout: runtimes/ is one level up from the
        // host folder, with native libraries under runtimes/<rid>/native/.
        var hostFolder = Path.Combine(_tempDir, "bin-11x");
        Directory.CreateDirectory(hostFolder);
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        var nativeDir = Path.Combine(_tempDir, "runtimes", rid, "native");
        Directory.CreateDirectory(nativeDir);

        // Platform-conventional native filename. The probe accepts either
        // the bare name OR the full filename — we test the bare-name path
        // (which is what P/Invoke typically passes).
        string fileName, probeName;
        if (OperatingSystem.IsMacOS())        { fileName = "libe_sqlite3.dylib"; probeName = "e_sqlite3"; }
        else if (OperatingSystem.IsWindows()) { fileName = "e_sqlite3.dll"; probeName = "e_sqlite3"; }
        else if (OperatingSystem.IsLinux())   { fileName = "libe_sqlite3.so"; probeName = "e_sqlite3"; }
        else throw new PlatformNotSupportedException();

        var stubPath = Path.Combine(nativeDir, fileName);
        File.WriteAllBytes(stubPath, Array.Empty<byte>());

        using var ctx = new ConcordHostLoadContext(hostFolder);

        var resolved = ctx.TryResolveNativePath(probeName, out var path);

        resolved.Should().BeTrue();
        path.Should().Be(stubPath);
    }

    [Fact]
    public void TryResolveNativePath_ReturnsFalse_WhenNoNativeFolderExists()
    {
        var hostFolder = Path.Combine(_tempDir, "bin-11x");
        Directory.CreateDirectory(hostFolder);

        using var ctx = new ConcordHostLoadContext(hostFolder);

        var resolved = ctx.TryResolveNativePath("nonexistent_lib", out var path);

        resolved.Should().BeFalse();
        path.Should().BeNull();
    }

    [Fact]
    public void TryResolveNativePath_FlatHostFolderFallback_WhenNoRuntimesFolder()
    {
        // Some packages drop natives flat in the host folder (no runtimes/
        // subtree). The probe's last-ditch fallback handles that case.
        var hostFolder = Path.Combine(_tempDir, "bin-11x");
        Directory.CreateDirectory(hostFolder);

        var flatName = OperatingSystem.IsMacOS() ? "libflatnative.dylib"
                     : OperatingSystem.IsWindows() ? "flatnative.dll"
                     : "libflatnative.so";
        var flatPath = Path.Combine(hostFolder, flatName);
        File.WriteAllBytes(flatPath, Array.Empty<byte>());

        using var ctx = new ConcordHostLoadContext(hostFolder);

        // Pass the full filename — flat probe doesn't do name decoration.
        var resolved = ctx.TryResolveNativePath(flatName, out var path);

        resolved.Should().BeTrue();
        path.Should().Be(flatPath);
    }
}
