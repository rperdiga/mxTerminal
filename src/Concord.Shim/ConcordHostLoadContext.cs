using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Concord.Shim;

/// <summary>
/// AssemblyLoadContext for the version-specific host. Resolution order:
///
/// <list type="number">
/// <item><b>Concord.* — local first.</b> Two-copy parity: shim's copy in
/// default ALC and host's copy here have isolated static state. The
/// hash-gate in MergeHostsForShim.targets enforces byte-identical content,
/// so behavior is parity but instance state (HostServices, HostContext)
/// is correctly per-context.</item>
/// <item><b>Anything SP already has loaded — defer to default.</b>
/// Catches Mendix.StudioPro.ExtensionsAPI (Q2 spike invariant on type
/// identity at the API boundary), Eto (SP's UI toolkit; Application.Instance
/// MUST be SP's singleton, not a fresh null in a separate-context copy),
/// Mendix.Modeler.* (SP internals reachable through ExtensionsAPI return
/// types), and BCL types the runtime already provides. Dynamic check via
/// <see cref="AssemblyLoadContext.Default.Assemblies"/> rather than a
/// hardcoded prefix list, so any future SP-loaded assembly is handled
/// without code change.</item>
/// <item><b>Otherwise — local first, then default fallback.</b> NuGet
/// runtime libraries the host ships but SP doesn't load
/// (Microsoft.Extensions.*, Microsoft.AspNetCore.*, SQLitePCLRaw.*,
/// System.ServiceModel.*, etc.) load from bin-{Nx}/ here.</item>
/// </list>
///
/// One instance per process is sufficient (and intended) — the shim binds
/// to a host on first load and stays bound for the process lifetime, per
/// spec §"Non-goals". Multiple instances would double-load the host
/// assembly and double-state HostServices/HostContext.
/// </summary>
internal sealed class ConcordHostLoadContext : AssemblyLoadContext, IDisposable
{
    private readonly string _hostFolder;

    public ConcordHostLoadContext(string hostFolder)
        : base(name: $"ConcordHost@{hostFolder}", isCollectible: false)
    {
        _hostFolder = hostFolder;
        Resolving += OnResolving;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // The CLR calls Load() FIRST when this context needs to resolve a
        // dependency of an assembly loaded into it. Resolving fires only
        // when Load returns null AND default-ALC fallback also misses.
        // For some resolution paths (notably .NET 8 JIT-time TypeRef
        // resolution from inside Activator-loaded code), the Resolving
        // fallback isn't reliably reached. We run the full rule set here
        // so every dep follows the same logic regardless of which CLR
        // resolution code path is taken.
        return Resolve(assemblyName);
    }

    private Assembly? OnResolving(AssemblyLoadContext _, AssemblyName name)
        => Resolve(name);

    private Assembly? Resolve(AssemblyName name)
    {
        var simpleName = name.Name;
        ShimLog.Info($"Resolve fired for {simpleName} (full: {name.FullName})");

        // 1. Concord.* — load locally if a copy exists. Intentional two-copy
        //    parity: shim's copy in default ALC and host's copy here have
        //    isolated static state, enforced byte-identical by the hash gate
        //    in MergeHostsForShim.targets. Without this exemption, the
        //    dynamic-default check below would forward host requests for
        //    Concord.Core back to the shim's copy, collapsing the two-copy
        //    design.
        if (IsConcordOwned(simpleName))
        {
            var concordLocal = Path.Combine(_hostFolder, simpleName + ".dll");
            if (File.Exists(concordLocal))
            {
                ShimLog.Info($"Resolved {simpleName} from {concordLocal} into {Name}");
                return LoadFromAssemblyPath(concordLocal);
            }
        }

        // 2. Any assembly already loaded in SP's default ALC — return that
        //    EXACT loaded assembly (not null). Catches Mendix.StudioPro.ExtensionsAPI
        //    (the SP↔shim API boundary — Q2 spike invariant on type identity),
        //    Eto (SP's UI toolkit; Application.Instance must be SP's singleton,
        //    NOT a fresh null static in our local copy), Mendix.Modeler.*
        //    (SP internals reachable through ExtensionsAPI return types), and
        //    BCL types the runtime already loaded.
        //
        //    Returning the loaded Assembly directly (vs returning null and
        //    relying on CLR's default-ALC fallback) bypasses the CLR's strict
        //    version-match check. SP may ship a different version than the
        //    package we compiled against — e.g., SP 11.10 ships Eto v2.8.0.0
        //    but Eto.Forms NuGet 2.9.x produces a "v2.9.0.0" reference. Strict
        //    fallback fails on the version mismatch (FileNotFoundException).
        //    Explicit return tells the CLR "use this assembly, period" —
        //    works as long as the API surface our code calls is present in
        //    SP's version (non-strong-named assemblies tolerate this).
        //    Dynamic check (instead of a hardcoded prefix list) so future
        //    SP-loaded assemblies are handled automatically.
        var defaultLoaded = TryFindInDefault(simpleName);
        if (defaultLoaded is not null)
            return defaultLoaded;

        // 3. Probe the host folder. NuGet-distributed runtime libraries the
        //    host depends on but SP didn't load (Microsoft.Extensions.*,
        //    Microsoft.AspNetCore.*, Microsoft.Data.*, SQLitePCLRaw.*,
        //    System.ServiceModel.*, System.Security.Cryptography.Pkcs/Xml,
        //    etc.) land here.
        var candidate = Path.Combine(_hostFolder, simpleName + ".dll");
        if (File.Exists(candidate))
        {
            ShimLog.Info($"Resolved {simpleName} from {candidate} into {Name}");
            return LoadFromAssemblyPath(candidate);
        }

        // 4. Fall through to default context. BCL types like System.Runtime
        //    that the runtime provides without a host-folder copy resolve
        //    here through the default ALC's own probing.
        //
        //    NOTE: there used to be an AssemblyDependencyResolver-based
        //    priority-4 here that read the host's .deps.json to handle
        //    runtimes/<rid>/lib/<tfm>/ RID-specific managed DLLs. Its
        //    constructor invokes native corehost_resolve_component_dependencies,
        //    which has a precondition (fxr_path populated via corehost_main)
        //    that Studio Pro's macOS launcher doesn't satisfy — throwing
        //    InvalidArgFailure -2147450750 and breaking MEF activation on
        //    Mac. Removed entirely. None of Concord's actual managed
        //    dependencies use the runtimes/<rid>/lib/ layout — they're all
        //    flat in the host folder, hit by priority-3 above. Native
        //    binaries (libe_sqlite3.dylib etc.) are handled by the
        //    LoadUnmanagedDll override below, not by this method.
        return null;
    }

    private static bool IsConcordOwned(string? name)
        => name?.StartsWith("Concord.", StringComparison.Ordinal) == true;

    private static Assembly? TryFindInDefault(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var a in Default.Assemblies)
        {
            if (string.Equals(a.GetName().Name, name, StringComparison.Ordinal))
                return a;
        }
        return null;
    }

    // Native unmanaged DLL resolution. Defensive against the same hostpolicy
    // class of failure that took out AssemblyDependencyResolver on Mac:
    // .NET's default unmanaged search uses similar hostpolicy state that
    // Studio Pro's macOS launcher doesn't fully initialise. Explicit probe
    // of runtimes/<rid>/native/ with RID fallback makes resolution
    // deterministic regardless of how .NET was hosted.
    //
    // The deployed snapshot layout is:
    //     extensions/Concord/
    //       Concord.Shim.dll
    //       bin-{10,11}x/   ← _hostFolder
    //       runtimes/<rid>/native/<libname>.dylib (or .dll / .so)
    //
    // So the runtimes/ folder is one level UP from _hostFolder. Some
    // build configurations may also drop it alongside; we probe both.
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        ShimLog.Info($"LoadUnmanagedDll fired for {unmanagedDllName}");
        if (TryResolveNativePath(unmanagedDllName, out var path))
        {
            ShimLog.Info($"Resolved native {unmanagedDllName} from {path} into {Name}");
            return LoadUnmanagedDllFromPath(path!);
        }
        // IntPtr.Zero signals "I didn't find it; fall back to the default
        // native search (OS-default paths, executable's directory, etc.)".
        return IntPtr.Zero;
    }

    internal bool TryResolveNativePath(string unmanagedDllName, out string? path)
    {
        // Production layout: runtimes/ is one level up from the host folder.
        // Normalize via GetFullPath so the returned path doesn't carry a
        // ".." segment, which would confuse callers logging the resolution
        // result and break string-based assertions in tests.
        var runtimesDir = Path.GetFullPath(Path.Combine(_hostFolder, "..", "runtimes"));
        if (!Directory.Exists(runtimesDir))
            runtimesDir = Path.GetFullPath(Path.Combine(_hostFolder, "runtimes"));

        if (Directory.Exists(runtimesDir))
        {
            foreach (var probe in NativeProbePaths(runtimesDir, unmanagedDllName))
            {
                if (File.Exists(probe))
                {
                    path = probe;
                    return true;
                }
            }
        }
        // Last-ditch: flat in host folder (some packages drop natives there).
        var flat = Path.GetFullPath(Path.Combine(_hostFolder, unmanagedDllName));
        if (File.Exists(flat))
        {
            path = flat;
            return true;
        }
        path = null;
        return false;
    }

    private static IEnumerable<string> NativeProbePaths(string runtimesDir, string name)
    {
        foreach (var rid in RidFallbackChain())
        {
            var native = Path.Combine(runtimesDir, rid, "native");
            if (!Directory.Exists(native)) continue;
            // Caller may pass either the bare name (e.g. "e_sqlite3") or
            // the full filename (e.g. "libe_sqlite3.dylib"). Try both.
            yield return Path.Combine(native, name);
            if (OperatingSystem.IsMacOS())
            {
                yield return Path.Combine(native, "lib" + name + ".dylib");
                yield return Path.Combine(native, name + ".dylib");
            }
            else if (OperatingSystem.IsWindows())
            {
                yield return Path.Combine(native, name + ".dll");
            }
            else if (OperatingSystem.IsLinux())
            {
                yield return Path.Combine(native, "lib" + name + ".so");
                yield return Path.Combine(native, name + ".so");
            }
        }
    }

    private static IEnumerable<string> RidFallbackChain()
    {
        // RuntimeInformation.RuntimeIdentifier returns the most specific RID
        // (osx-arm64 on Apple Silicon, osx-x64 on Intel Macs, win-x64 on
        // x64 Windows). Walk the RID graph in specificity order so a
        // package that only shipped a generic-RID native still resolves.
        var current = RuntimeInformation.RuntimeIdentifier;
        yield return current;

        if (OperatingSystem.IsMacOS())
        {
            yield return "osx";
            yield return "unix";
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return "linux";
            yield return "unix";
        }
        else if (OperatingSystem.IsWindows())
        {
            yield return "win";
        }

        yield return "any";
    }

    public void Dispose()
    {
        // AssemblyLoadContext is non-collectible (we set isCollectible: false
        // because the .NET CoreCLR + native interop layer in Studio Pro is
        // not designed to support collectible contexts safely under all the
        // unmanaged code paths Studio Pro's loader takes). Dispose just
        // detaches the Resolving handler so any further attempts fail-fast.
        Resolving -= OnResolving;
    }
}
