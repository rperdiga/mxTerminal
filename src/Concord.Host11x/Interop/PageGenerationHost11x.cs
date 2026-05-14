namespace Concord.Host11x.Interop;

using System.Security.Cryptography;
using System.Text;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using Mendix.StudioPro.ExtensionsAPI.Model.Pages;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Terminal.Interop;

/// <summary>
/// Implements IPageGenerationHost against the 11.6.2 ExtensionsAPI surface.
/// Bodies ported from MendixAdditionalTools.GenerateOverviewPages / DeleteDocument
/// (SPMCP source), with JSON parsing stripped and Mendix modeling work retained.
/// </summary>
public sealed class PageGenerationHost11x : IPageGenerationHost
{
    private readonly IModel _model;
    private readonly IPageGenerationService _pageGen;
    private readonly INavigationManagerService _nav;

    public PageGenerationHost11x(
        IModel model,
        IPageGenerationService pageGen,
        INavigationManagerService nav)
    {
        _model = model;
        _pageGen = pageGen;
        _nav = nav;
    }

    // ── IPageGenerationHost ──────────────────────────────────────────────────

    public PageGenerationResult GenerateOverviewPages(PageGenerationRequest request)
    {
        // Resolve module
        var module = _model.Root.GetModules()
            .FirstOrDefault(m => string.Equals(m.Name, request.ModuleName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Module '{request.ModuleName}' not found.");

        if (module.DomainModel is null)
            throw new InvalidOperationException($"Module '{request.ModuleName}' has no domain model.");

        var allEntities = module.DomainModel.GetEntities().ToList();
        var toGenerate = allEntities
            .Where(e => request.EntityNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (toGenerate.Count == 0)
            return new PageGenerationResult(
                Success: false,
                CreatedPageNames: Array.Empty<string>(),
                Warnings: Array.Empty<string>(),
                Error: $"None of the requested entities were found in module '{request.ModuleName}'.");

        // Generate overview pages (IPageGenerationService wraps the transaction internally)
        List<IPage> generatedPages;
        using (var tx = _model.StartTransaction("Generate overview pages"))
        {
            generatedPages = _pageGen.GenerateOverviewPages(module, toGenerate, request.GenerateIndexSnippet).ToList();

            // Add overview pages to responsive web navigation
            var overviewPages = generatedPages
                .Where(p => p.Name.Contains("overview", StringComparison.InvariantCultureIgnoreCase))
                .Select(p => (p.Name, p))
                .ToArray();

            if (overviewPages.Length > 0)
                _nav.PopulateWebNavigationWith(_model, overviewPages);

            tx.Commit();
        }

        // Collect warnings about potentially broken widget bindings
        var warnings = new List<string>();
        var hasEnumAttrs = toGenerate.Any(e =>
            e.GetAttributes().Any(a => a.Type?.GetType().Name?.Contains("Enumeration") == true));
        var hasAssocs = toGenerate.Any(e => e.GetAssociations(AssociationDirection.Both, null).Any());
        if (hasEnumAttrs)
            warnings.Add("Some entities have enumeration-typed attributes which may generate broken widget bindings (CE1613). Please verify in Studio Pro.");
        if (hasAssocs)
            warnings.Add("Some entities have associations which may generate broken reference widget bindings. Please verify in Studio Pro.");

        return new PageGenerationResult(
            Success: true,
            CreatedPageNames: generatedPages.Select(p => p.Name).ToList(),
            Warnings: warnings,
            Error: null);
    }

    public bool DeleteDocument(DocumentId document)
    {
        foreach (var module in _model.Root.GetModules())
        {
            // Check root level
            var doc = module.GetDocuments()
                .FirstOrDefault(d => string.Equals(d.Name, document.QualifiedName.Split('.').LastOrDefault(), StringComparison.OrdinalIgnoreCase)
                                  && ParseId(d.Id) == document.Value);
            IFolderBase? parent = doc is not null ? module : null;

            // Search subfolders if not found at root
            if (doc is null)
            {
                var found = FindDocumentWithParent(module, document.Value);
                doc = found.doc;
                parent = found.parent;
            }

            if (doc is not null && parent is not null)
            {
                using var tx = _model.StartTransaction($"Delete document '{doc.Name}'");
                parent.RemoveDocument(doc);
                tx.Commit();
                return true;
            }
        }
        return false;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static Guid ParseId(string id)
        => Guid.TryParse(id, out var guid) ? guid : GuidFromString(id);

    private static Guid GuidFromString(string s)
    {
        var bytes = new byte[16];
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(s));
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    private static (IDocument? doc, IFolderBase? parent) FindDocumentWithParent(IFolderBase folder, Guid targetId)
    {
        foreach (var sub in folder.GetFolders())
        {
            var doc = sub.GetDocuments()
                .FirstOrDefault(d => ParseId(d.Id) == targetId);
            if (doc is not null)
                return (doc, sub);
            var found = FindDocumentWithParent(sub, targetId);
            if (found.doc is not null)
                return found;
        }
        // Also check root documents of this folder
        var rootDoc = folder.GetDocuments().FirstOrDefault(d => ParseId(d.Id) == targetId);
        return rootDoc is not null ? (rootDoc, folder) : (null, null);
    }
}
