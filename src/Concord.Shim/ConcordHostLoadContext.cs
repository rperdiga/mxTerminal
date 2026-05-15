using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Concord.Shim;

/// <summary>
/// AssemblyLoadContext for the version-specific host. Resolves
/// Mendix.StudioPro.ExtensionsAPI and System.* / Microsoft.* through the
/// default context (so Studio Pro's already-loaded copy is reused and CLR
/// type identity is preserved across the boundary). Resolves everything
/// else from the configured bin-{Nx}/ folder.
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

    protected override Assembly? Load(AssemblyName assemblyName) =>
        // The Load override fires for the host assembly itself when something
        // inside the context asks for it. Default to falling through to
        // OnResolving so the shared-types rules apply uniformly.
        null;

    private Assembly? OnResolving(AssemblyLoadContext _, AssemblyName name)
    {
        // Shared types — defer to default context. This preserves CLR
        // type identity for any type Studio Pro hands across the boundary.
        if (IsSharedAssembly(name.Name))
            return null;

        // Try the host folder.
        var candidate = Path.Combine(_hostFolder, name.Name + ".dll");
        if (File.Exists(candidate))
        {
            ShimLog.Info($"Resolved {name.Name} from {candidate} into {Name}");
            return LoadFromAssemblyPath(candidate);
        }

        // Try via the dependency resolver as a secondary lookup (handles
        // runtime/<rid>/ subfolders for native interop etc.).
        var resolved = _resolver?.ResolveAssemblyToPath(name);
        if (resolved is not null && File.Exists(resolved))
        {
            ShimLog.Info($"Resolved {name.Name} from {resolved} via dependency resolver into {Name}");
            return LoadFromAssemblyPath(resolved);
        }

        ShimLog.Warn($"Could not resolve {name.Name} in {Name}; deferring to default context.");
        return null;
    }

    private static bool IsSharedAssembly(string? name)
    {
        if (name is null) return false;
        return name.StartsWith("Mendix.StudioPro.ExtensionsAPI", StringComparison.Ordinal)
            || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || name == "System"
            || name == "netstandard"
            || name == "mscorlib";
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
