namespace Terminal.Interop;

/// <summary>
/// Host-side identifier for a Mendix module. The Guid maps to the host's
/// internal IModule instance; Core never resolves it directly.
/// </summary>
public readonly record struct ModuleId(Guid Value, string Name);

/// <summary>
/// Host-side identifier for a Mendix document (entity, microflow, page, etc.).
/// QualifiedName is "ModuleName.DocumentName".
/// </summary>
public readonly record struct DocumentId(Guid Value, string QualifiedName);

/// <summary>
/// Host-side identifier for a folder within a module. Modules form a tree
/// of folders containing documents.
/// </summary>
public readonly record struct FolderId(Guid Value, string Path);

public readonly record struct ProjectInfo(
    string Name,
    string DirectoryPath,
    string? MendixVersion,
    string? AppId);

/// <summary>
/// Project-level runtime configuration entry, as exposed by IConfigurationSettings.
/// </summary>
public record ConfigurationSetting(
    string Name,
    bool IsActive,
    string? DatabaseType,
    string? DatabaseConnectionString,
    IReadOnlyDictionary<string, string> CustomSettings);

public record RuntimeSetting(
    string Key,
    string? Value,
    string? Description);

/// <summary>
/// Wraps Mendix.StudioPro.ExtensionsAPI.Model.IModel for SPMCP tools and
/// handlers that need project/module/document traversal and project-level
/// settings. Heavier modeling operations (entity create, microflow author,
/// etc.) belong on the more specific host interfaces.
/// </summary>
public interface IModelHost
{
    // --- Project / module / document traversal ---
    ProjectInfo GetProjectInfo();
    IReadOnlyList<ModuleId> ListModules();
    ModuleId? GetModuleByName(string moduleName);
    IReadOnlyList<DocumentId> ListModuleDocuments(ModuleId moduleId, string? documentTypeFilter = null);
    IReadOnlyList<DocumentId> ListAllDocuments(string? documentTypeFilter = null);
    DocumentId? GetDocumentByQualifiedName(string qualifiedName);

    // --- Folder tree (within a module) ---
    IReadOnlyList<FolderId> ListFolders(ModuleId moduleId);
    FolderId? CreateFolder(ModuleId moduleId, string parentFolderPath, string folderName);
    bool DeleteFolder(FolderId folder);
    bool MoveDocument(DocumentId document, FolderId? newFolder);

    // --- Project-level settings (read-only mostly; writes happen via IDomainModelHost) ---
    IReadOnlyList<RuntimeSetting> ReadRuntimeSettings();
    bool WriteRuntimeSetting(string key, string? value);
    IReadOnlyList<ConfigurationSetting> ReadConfigurations();
    bool SetActiveConfiguration(string configurationName);

    // --- Document exclusion (the SPMCP exclude_document tool) ---
    bool SetDocumentExcluded(DocumentId document, bool excluded);

    // --- Save / persistence ---
    Task SaveAsync(CancellationToken ct = default);
}
