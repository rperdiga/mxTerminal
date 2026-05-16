using System.IO;
using System.Reflection;
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
    private readonly AssemblyDependencyResolver? _resolver;

    public ConcordHostLoadContext(string hostFolder)
        : base(name: $"ConcordHost@{hostFolder}", isCollectible: false)
    {
        _hostFolder = hostFolder;
        // AssemblyDependencyResolver reads the .deps.json of a primary
        // assembly to resolve its dependency graph. We construct it ONLY
        // when the host DLL is already on disk (the File.Exists guard
        // below) — the resolver constructor immediately reads the adjacent
        // .deps.json and would throw otherwise. Phase 3's HostKickstart
        // must therefore ensure the bin folder is populated before
        // constructing this context.
        var likelyHostDll = Path.Combine(hostFolder, "Concord.Host10x.dll");
        if (!File.Exists(likelyHostDll))
            likelyHostDll = Path.Combine(hostFolder, "Concord.Host11x.dll");
        if (File.Exists(likelyHostDll))
            _resolver = new AssemblyDependencyResolver(likelyHostDll);

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

        // 4. AssemblyDependencyResolver — handles runtimes/<rid>/ subfolders
        //    for native interop, satellite resources, etc.
        var resolved = _resolver?.ResolveAssemblyToPath(name);
        if (resolved is not null && File.Exists(resolved))
        {
            ShimLog.Info($"Resolved {simpleName} from {resolved} via dependency resolver into {Name}");
            return LoadFromAssemblyPath(resolved);
        }

        // 5. Fall through to default context. BCL types like System.Runtime
        //    that the runtime provides without a host-folder copy resolve
        //    here through the default ALC's own probing.
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
