namespace Concord.Host10x.Interop;

using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Pages;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Terminal.Interop;

/// <summary>
/// Implements INavigationHost against the 10.21.1 ExtensionsAPI surface.
/// Bodies ported from MendixAdditionalTools.ManageNavigation (SPMCP source),
/// with JSON parsing stripped and Mendix modeling work retained.
///
/// INavigationManagerService in 10.21.1 exposes only PopulateWebNavigationWith.
/// Read/remove/set operations are implemented as best-effort using the typed
/// model traversal. Profiles that cannot be resolved are returned as empty.
/// </summary>
public sealed class NavigationHost10x : INavigationHost
{
    private readonly IModel _model;
    private readonly INavigationManagerService _nav;

    public NavigationHost10x(
        IModel model,
        INavigationManagerService nav)
    {
        _model = model;
        _nav = nav;
    }

    // ── INavigationHost ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns navigation profile stubs. The 10.21.1 ExtensionsAPI surface exposes
    /// INavigationManagerService.PopulateWebNavigationWith but does not expose a
    /// read-back API for navigation profiles. This method returns an empty list.
    /// Full navigation introspection requires the IUntypedModelAccessService path.
    /// </summary>
    public IReadOnlyList<NavigationProfile> ListProfiles()
        => Array.Empty<NavigationProfile>();

    /// <summary>
    /// Returns null — the 10.21.1 ExtensionsAPI does not expose a read-back API
    /// for navigation item content. Use IUntypedModelHost for inspection.
    /// </summary>
    public NavigationProfile? ReadProfile(string profileName) => null;

    public void AddItem(string profileName, NavigationItem item)
        => AddItems(profileName, new[] { item });

    public void AddItems(string profileName, IReadOnlyList<NavigationItem> items)
    {
        // Resolve each item's target page from its DocumentQualifiedName
        var resolvedPages = new List<(string caption, IPage page)>();
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.DocumentQualifiedName))
                throw new InvalidOperationException(
                    $"Navigation item '{item.Caption}' has no DocumentQualifiedName — cannot resolve page.");

            var page = FindPageByQualifiedName(item.DocumentQualifiedName)
                ?? throw new InvalidOperationException(
                    $"Page '{item.DocumentQualifiedName}' not found in the model.");

            resolvedPages.Add((item.Caption, page));
        }

        using var tx = _model.StartTransaction($"Add {resolvedPages.Count} navigation item(s) to web navigation");
        _nav.PopulateWebNavigationWith(_model, resolvedPages.ToArray());
        tx.Commit();
    }

    /// <summary>
    /// Remove is not supported via INavigationManagerService in 10.21.1.
    /// This method is a no-op. Use Studio Pro UI to remove navigation items.
    /// </summary>
    public void RemoveItem(string profileName, string caption)
    {
        // INavigationManagerService in 10.21.1 exposes only PopulateWebNavigationWith.
        // No remove API is available. No-op per the interface contract.
    }

    /// <summary>
    /// Re-adds the item with the new target. Since PopulateWebNavigationWith
    /// appends (does not replace), this results in a duplicate entry;
    /// users should remove the old entry via the Studio Pro UI.
    /// </summary>
    public void SetItemTarget(string profileName, string caption, string microflowOrPageQualifiedName)
    {
        var page = FindPageByQualifiedName(microflowOrPageQualifiedName)
            ?? throw new InvalidOperationException(
                $"Page '{microflowOrPageQualifiedName}' not found — microflow targets are not supported via INavigationManagerService.");

        using var tx = _model.StartTransaction($"Set target of navigation item '{caption}'");
        _nav.PopulateWebNavigationWith(_model, new[] { (caption, page) });
        tx.Commit();
    }

    /// <summary>
    /// Icon setting is not exposed by INavigationManagerService in 10.21.1. No-op.
    /// </summary>
    public void SetItemIcon(string profileName, string caption, string? iconQualifiedName)
    {
        // INavigationManagerService.PopulateWebNavigationWith does not accept icons.
        // No-op.
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private IPage? FindPageByQualifiedName(string qualifiedName)
    {
        var dot = qualifiedName.IndexOf('.');
        if (dot < 0) return null;
        var moduleName = qualifiedName[..dot];
        var pageName = qualifiedName[(dot + 1)..];

        var module = _model.Root.GetModules()
            .FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (module is null) return null;

        var page = module.GetDocuments().OfType<IPage>()
            .FirstOrDefault(p => string.Equals(p.Name, pageName, StringComparison.OrdinalIgnoreCase));
        if (page is not null) return page;

        // Search sub-folders recursively
        foreach (var folder in GetAllFolders(module))
        {
            page = folder.GetDocuments().OfType<IPage>()
                .FirstOrDefault(p => string.Equals(p.Name, pageName, StringComparison.OrdinalIgnoreCase));
            if (page is not null) return page;
        }
        return null;
    }

    private static IEnumerable<IFolder> GetAllFolders(IFolderBase parent)
    {
        foreach (var folder in parent.GetFolders())
        {
            yield return folder;
            foreach (var sub in GetAllFolders(folder))
                yield return sub;
        }
    }
}
