using Mendix.StudioPro.ExtensionsAPI.Services;

namespace Concord.Shim;

/// <summary>
/// Wraps Studio Pro's <see cref="IExtensionFileService"/> so the inner
/// host's calls are re-dispatched through the shim's assembly.
///
/// <para>
/// Why: SP's <c>ExtensionFileService.ResolvePath</c> internally uses
/// <c>Assembly.GetCallingAssembly()</c> to identify which extension's
/// deploy folder to resolve relative to. The lookup table is keyed by
/// the assembly SP MEF-registered. Only <see cref="Concord.Shim"/> is
/// registered; the inner host (loaded via <c>Activator.CreateInstance</c>
/// from <see cref="ConcordHostLoadContext"/>) is not. A direct call from
/// the inner host's assembly would throw <c>KeyNotFoundException</c>.
/// </para>
///
/// <para>
/// This wrapper lives in the shim assembly, so when it invokes
/// <c>_inner.ResolvePath(...)</c>, the assembly SP observes via
/// <c>GetCallingAssembly()</c> is <see cref="Concord.Shim"/> — which IS
/// registered, and IS mapped to <c>extensions/Concord/</c> (the merged
/// deploy folder). Path segments like <c>"skills"</c>, <c>"rules"</c>,
/// and <c>"wwwroot/index.html"</c> resolve to the right files.
/// </para>
/// </summary>
internal sealed class ShimExtensionFileService : IExtensionFileService
{
    private readonly IExtensionFileService _inner;

    public ShimExtensionFileService(IExtensionFileService inner)
        => _inner = inner;

    public string ResolvePath(params string[] pathSegments)
        => _inner.ResolvePath(pathSegments);
}
