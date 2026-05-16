using System;
using System.IO;
using System.Reflection;

namespace Concord.Shim;

/// <summary>
/// Process-wide idempotent host bootstrap. First call from any shim
/// [Export] activates the AssemblyLoadContext, loads
/// Concord.Host{N}x.dll, and instantiates Host{N}xEntry once (which
/// fires HostContext.Initialize, HostServices.Register, and
/// ToolCatalog population — all via the host's static cctor + ctor).
///
/// Subsequent calls are O(1) — return immediately.
///
/// Thread safety: a process-wide lock guards EnsureLoaded. The shim's
/// three [Export]s all call EnsureLoaded; only one will win the race;
/// the other two block briefly and return.
/// </summary>
internal static class HostKickstart
{
    private static readonly object _gate = new();
    private static volatile bool _loaded;
    private static ConcordHostLoadContext? _context;
    private static Assembly? _hostAssembly;
    private static Exception? _failedException;

    // Testing seams. NEVER used in production — production resolves these
    // values from RuntimeHostLocator + the convention below.
    private static string? _testHostFolder;
    private static string? _testHostAssemblyName;
    private static string? _testEntryTypeName;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        if (_failedException is not null) throw new InvalidOperationException(
            "HostKickstart previously failed; not retrying. See inner exception.",
            _failedException);
        lock (_gate)
        {
            if (_loaded) return;
            if (_failedException is not null) throw new InvalidOperationException(
                "HostKickstart previously failed; not retrying. See inner exception.",
                _failedException);
            try
            {
                var (hostFolder, version) = ShimLog.Timed("HostKickstart.ResolveHostFolder",
                    () => ResolveHostFolder());
                if (!Directory.Exists(hostFolder))
                    throw new DirectoryNotFoundException(
                        $"Concord host folder not found: {hostFolder}. " +
                        $"Studio Pro reports version '{version}'. " +
                        $"The .mxmodule may be corrupted or partially installed.");

                ShimLog.Info($"HostKickstart: SP version='{version}', hostFolder={hostFolder}");

                var hostAssemblyName = ResolveHostAssemblyName(hostFolder);
                var hostDll = Path.Combine(hostFolder, hostAssemblyName + ".dll");

                _context = ShimLog.Timed("HostKickstart.BuildLoadContext",
                    () => new ConcordHostLoadContext(hostFolder));

                _hostAssembly = ShimLog.Timed("HostKickstart.LoadHostAssembly",
                    () => _context.LoadFromAssemblyPath(hostDll));

                // Resolve and instantiate Host{N}xEntry. Parameterless ctor —
                // the entry type's ImportingConstructor takes no MEF imports
                // (verified by reading both Host10xEntry.cs and Host11xEntry.cs).
                var entryTypeName = ResolveEntryTypeName(hostAssemblyName);
                var entryType = _hostAssembly.GetType(entryTypeName, throwOnError: true)!;

                ShimLog.Timed("HostKickstart.InstantiateEntry", () =>
                {
                    Activator.CreateInstance(entryType);
                    return 0;
                });

                _loaded = true;
            }
            catch (Exception ex)
            {
                _failedException = ex;
                throw;
            }
        }
    }

    public static Type? ResolveHostType(string fullyQualifiedTypeName)
    {
        EnsureLoaded();
        return _hostAssembly!.GetType(fullyQualifiedTypeName);
    }

    /// <summary>
    /// Returns the loaded host's assembly name ("Concord.Host10x" or
    /// "Concord.Host11x"). Triggers EnsureLoaded if not already loaded;
    /// throws if the assembly name doesn't match either expected host.
    ///
    /// Centralises the probe each shim's ResolveInnerTypeName() previously
    /// duplicated. If a future host variant is introduced, only this one
    /// place needs updating.
    /// </summary>
    public static string LoadedHostAssemblyName
    {
        get
        {
            EnsureLoaded();
            var name = _hostAssembly?.GetName().Name
                ?? throw new InvalidOperationException("EnsureLoaded did not populate _hostAssembly.");
            return name switch
            {
                "Concord.Host10x" => "Concord.Host10x",
                "Concord.Host11x" => "Concord.Host11x",
                _ => throw new InvalidOperationException(
                    $"Loaded host assembly '{name}' is neither Concord.Host10x nor Concord.Host11x.")
            };
        }
    }

    /// <summary>
    /// Instantiates the named host type using positional args (typically
    /// the services captured by a shim [Export]'s [ImportingConstructor]).
    /// </summary>
    public static object CreateHostInstance(string fullyQualifiedTypeName, params object?[] ctorArgs)
    {
        var type = ResolveHostType(fullyQualifiedTypeName)
            ?? throw new InvalidOperationException(
                $"Host type '{fullyQualifiedTypeName}' not found in {_hostAssembly?.GetName().Name}.");
        try
        {
            return Activator.CreateInstance(type, ctorArgs)!;
        }
        catch (Exception ex)
        {
            ShimLog.Error($"Failed to instantiate host type '{fullyQualifiedTypeName}'", ex);
            throw;
        }
    }

    private static (string hostFolder, string version) ResolveHostFolder()
    {
        if (_testHostFolder is not null)
            return (_testHostFolder, "<test>");
        return RuntimeHostLocator.ResolveBinDirectory();
    }

    private static string ResolveHostAssemblyName(string hostFolder)
    {
        if (_testHostAssemblyName is not null) return _testHostAssemblyName;
        // Convention: bin-10x/ contains Concord.Host10x.dll; bin-11x/ contains
        // Concord.Host11x.dll. Trust the folder name.
        var name = Path.GetFileName(hostFolder);
        return name switch
        {
            "bin-10x" => "Concord.Host10x",
            "bin-11x" => "Concord.Host11x",
            _ => throw new InvalidOperationException(
                $"Unrecognized host folder name '{name}'; expected bin-10x or bin-11x.")
        };
    }

    private static string ResolveEntryTypeName(string hostAssemblyName)
    {
        if (_testEntryTypeName is not null) return _testEntryTypeName;
        return hostAssemblyName switch
        {
            "Concord.Host10x" => "Concord.Host10x.Host10xEntry",
            "Concord.Host11x" => "Concord.Host11x.Host11xEntry",
            _ => throw new InvalidOperationException($"No entry type mapping for {hostAssemblyName}.")
        };
    }

    // === Testing seams. Production callers never touch these. ===

    internal static void OverrideForTesting(string hostFolder, string hostAssemblyName, string entryTypeName)
    {
        _testHostFolder = hostFolder;
        _testHostAssemblyName = hostAssemblyName;
        _testEntryTypeName = entryTypeName;
    }

    internal static void ResetForTesting()
    {
        _loaded = false;
        _failedException = null;
        _context?.Dispose();
        _context = null;
        _hostAssembly = null;
        _testHostFolder = null;
        _testHostAssemblyName = null;
        _testEntryTypeName = null;
    }
}
