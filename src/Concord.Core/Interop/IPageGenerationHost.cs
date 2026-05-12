namespace Terminal.Interop;

/// <summary>
/// Request payload for the GenerateOverviewPages operation.
/// The host resolves module and entity names to their Mendix model objects;
/// Core never touches ExtensionsAPI types directly.
/// </summary>
public record PageGenerationRequest(
    string ModuleName,
    IReadOnlyList<string> EntityNames,
    bool GenerateIndexSnippet);

/// <summary>
/// Result returned by GenerateOverviewPages.
/// CreatedPageNames are simple document names (not qualified) matching
/// what the Mendix IPageGenerationService returns.
/// </summary>
public record PageGenerationResult(
    bool Success,
    IReadOnlyList<string> CreatedPageNames,
    IReadOnlyList<string> Warnings,
    string? Error);

/// <summary>
/// Wraps Mendix IPageGenerationService for SPMCP's GenerateOverviewPages tool
/// and the general DeleteDocument path that removes pages/microflows by DocumentId.
///
/// The host implementation resolves Mendix entities from EntityNames and delegates
/// to IPageGenerationService.GenerateOverviewPages; Core only sees plain strings.
/// </summary>
public interface IPageGenerationHost
{
    /// <summary>
    /// Generate overview pages for the specified entities in the given module.
    /// Internally calls IPageGenerationService.GenerateOverviewPages and then
    /// adds the generated overview pages to the responsive web navigation.
    /// </summary>
    PageGenerationResult GenerateOverviewPages(PageGenerationRequest request);

    /// <summary>
    /// Delete a document (page, microflow, etc.) identified by its DocumentId.
    /// Returns false if the document was not found or the deletion failed.
    /// </summary>
    bool DeleteDocument(DocumentId document);
}
