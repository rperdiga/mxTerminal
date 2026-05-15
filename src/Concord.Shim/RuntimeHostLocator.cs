using System.IO;
using System.Reflection;

namespace Concord.Shim;

/// <summary>
/// Maps Studio Pro versions to the shim's bin-{Nx}/ subdirectory and
/// resolves the absolute path to that subdirectory anchored on the shim
/// assembly's actual file location.
///
/// CRITICAL: anchors on Assembly.Location, NOT AppDomain.BaseDirectory.
/// Studio Pro deploys extensions to <project>/.mendix-cache/extensions-cache/
/// <guid>/, but AppDomain.BaseDirectory returns Studio Pro's install dir.
/// This was empirically verified during the Phase 0 spike (see
/// docs/superpowers/handoffs/2026-05-15-concord-shim-spike-findings.md
/// §"Probe bugs discovered" item B).
/// </summary>
internal static class RuntimeHostLocator
{
    public static string BinFolderName(string? studioProVersion)
    {
        if (string.IsNullOrWhiteSpace(studioProVersion)) return "bin-11x";
        if (!System.Version.TryParse(SplitOffPrerelease(studioProVersion), out var v))
            return "bin-11x"; // unknown / garbage
        return v.Major >= 11 ? "bin-11x" : "bin-10x";
    }

    public static (string binDir, string version) ResolveBinDirectory()
    {
        var anchorAssemblyDir = AssemblyLocationDir(typeof(RuntimeHostLocator));
        var version = Terminal.StudioProThemeProbe.StudioProVersionFromExePath();
        var binName = BinFolderName(version);
        return (ResolveBinDirectoryFromAnchor(anchorAssemblyDir, binName), version ?? "<unknown>");
    }

    public static string ResolveBinDirectoryFromAnchor(string anchorAssemblyDir, string binFolderName)
        => Path.Combine(anchorAssemblyDir, binFolderName);

    private static string AssemblyLocationDir(System.Type anchor)
        => Path.GetDirectoryName(anchor.Assembly.Location)
           ?? throw new System.InvalidOperationException(
              $"Could not resolve directory of {anchor.FullName} assembly.");

    private static string SplitOffPrerelease(string v)
    {
        var dashIndex = v.IndexOf('-');
        return dashIndex >= 0 ? v.Substring(0, dashIndex) : v;
    }
}
