namespace Concord.Host10x.Interop;

using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.Settings;
using Terminal.Interop;

/// <summary>
/// Implements IModelHost against the 10.21.1 ExtensionsAPI surface.
/// The body is byte-identical to ModelHost11x; both versions expose the same
/// IProject / IModule / IDocument / IFolder / IConfigurationSettings / IRuntimeSettings
/// surface (verified against 10.21.1 NuGet package via reflection).
/// </summary>
public sealed class ModelHost10x : IModelHost
{
    private readonly IModel _model;

    public ModelHost10x(IModel model) => _model = model;

    // --- Project / module / document traversal ---

    public ProjectInfo GetProjectInfo()
    {
        var project = _model.Root;
        return new ProjectInfo(
            Name: project.Name,
            DirectoryPath: project.DirectoryPath,
            // IProject in 10.21.1 does not expose ServerVersion or AppId.
            MendixVersion: null,
            AppId: null);
    }

    public IReadOnlyList<ModuleId> ListModules()
        => _model.Root.GetModules()
                      .Select(m => new ModuleId(ParseId(m.Id), m.Name))
                      .ToList();

    public ModuleId? GetModuleByName(string moduleName)
    {
        var module = _model.Root.GetModules()
            .FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        return module is null ? null : new ModuleId(ParseId(module.Id), module.Name);
    }

    public IReadOnlyList<DocumentId> ListModuleDocuments(ModuleId moduleId, string? documentTypeFilter = null)
    {
        var module = ResolveModule(moduleId);
        return CollectModuleDocuments(module, documentTypeFilter);
    }

    public IReadOnlyList<DocumentId> ListAllDocuments(string? documentTypeFilter = null)
    {
        var results = new List<DocumentId>();
        foreach (var module in _model.Root.GetModules())
            results.AddRange(CollectModuleDocuments(module, documentTypeFilter));
        return results;
    }

    public DocumentId? GetDocumentByQualifiedName(string qualifiedName)
    {
        // qualifiedName is expected as "ModuleName.DocumentName"
        var dot = qualifiedName.IndexOf('.');
        if (dot < 0)
            return null;

        var moduleName = qualifiedName[..dot];
        var docName = qualifiedName[(dot + 1)..];

        var module = _model.Root.GetModules()
            .FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (module is null)
            return null;

        var doc = FindDocumentInFolder(module, docName);
        if (doc is null)
            return null;

        return new DocumentId(ParseId(doc.Id), $"{module.Name}.{doc.Name}");
    }

    // --- Folder tree (within a module) ---

    public IReadOnlyList<FolderId> ListFolders(ModuleId moduleId)
    {
        var module = ResolveModule(moduleId);
        var result = new List<FolderId>();
        CollectFolders(module, string.Empty, result);
        return result;
    }

    public FolderId? CreateFolder(ModuleId moduleId, string parentFolderPath, string folderName)
    {
        var module = ResolveModule(moduleId);

        using var tx = _model.StartTransaction("Create folder");
        var newFolder = _model.Create<IFolder>();
        newFolder.Name = folderName;

        if (string.IsNullOrEmpty(parentFolderPath))
        {
            module.AddFolder(newFolder);
        }
        else
        {
            var parentFolder = FindFolderByPath(module, parentFolderPath);
            if (parentFolder is null)
                throw new InvalidOperationException($"Parent folder '{parentFolderPath}' not found in module '{module.Name}'");
            parentFolder.AddFolder(newFolder);
        }

        tx.Commit();
        var createdPath = string.IsNullOrEmpty(parentFolderPath) ? folderName : $"{parentFolderPath}/{folderName}";
        return new FolderId(ParseId(newFolder.Id), createdPath);
    }

    public bool DeleteFolder(FolderId folder)
    {
        foreach (var module in _model.Root.GetModules())
        {
            var found = FindFolderWithParent(module, folder.Value);
            if (found.Folder is not null)
            {
                using var tx = _model.StartTransaction($"Delete folder '{folder.Path}'");
                found.Parent.RemoveFolder(found.Folder);
                tx.Commit();
                return true;
            }
        }
        return false;
    }

    public bool MoveDocument(DocumentId document, FolderId? newFolder)
    {
        foreach (var module in _model.Root.GetModules())
        {
            var found = FindDocumentWithParent(module, document.Value);
            if (found.Document is not null)
            {
                IFolderBase target;
                if (newFolder is null)
                {
                    // Move to module root
                    target = module;
                }
                else
                {
                    var targetFolder = FindFolderById(module, newFolder.Value.Value);
                    if (targetFolder is null)
                        throw new InvalidOperationException($"Target folder '{newFolder.Value.Path}' not found");
                    target = targetFolder;
                }

                using var tx = _model.StartTransaction("Move document to folder");
                found.Parent.RemoveDocument(found.Document);
                target.AddDocument(found.Document);
                tx.Commit();
                return true;
            }
        }
        return false;
    }

    // --- Project-level settings ---

    public IReadOnlyList<RuntimeSetting> ReadRuntimeSettings()
    {
        var runtimeSettings = GetSettingsPart<IRuntimeSettings>();
        if (runtimeSettings is null)
            return Array.Empty<RuntimeSetting>();

        return new[]
        {
            new RuntimeSetting("AfterStartupMicroflow", runtimeSettings.AfterStartupMicroflow?.ToString(), "Microflow called after the application starts"),
            new RuntimeSetting("BeforeShutdownMicroflow", runtimeSettings.BeforeShutdownMicroflow?.ToString(), "Microflow called before the application shuts down"),
            new RuntimeSetting("HealthCheckMicroflow", runtimeSettings.HealthCheckMicroflow?.ToString(), "Microflow called by the health check endpoint"),
        };
    }

    public bool WriteRuntimeSetting(string key, string? value)
    {
        var runtimeSettings = GetSettingsPart<IRuntimeSettings>();
        if (runtimeSettings is null)
            return false;

        using var tx = _model.StartTransaction("Set runtime setting");

        switch (key)
        {
            case "AfterStartupMicroflow":
                runtimeSettings.AfterStartupMicroflow = string.IsNullOrEmpty(value)
                    ? null
                    : _model.ToQualifiedName<Mendix.StudioPro.ExtensionsAPI.Model.Microflows.IMicroflow>(value);
                break;
            case "BeforeShutdownMicroflow":
                runtimeSettings.BeforeShutdownMicroflow = string.IsNullOrEmpty(value)
                    ? null
                    : _model.ToQualifiedName<Mendix.StudioPro.ExtensionsAPI.Model.Microflows.IMicroflow>(value);
                break;
            case "HealthCheckMicroflow":
                runtimeSettings.HealthCheckMicroflow = string.IsNullOrEmpty(value)
                    ? null
                    : _model.ToQualifiedName<Mendix.StudioPro.ExtensionsAPI.Model.Microflows.IMicroflow>(value);
                break;
            default:
                return false;
        }

        tx.Commit();
        return true;
    }

    public IReadOnlyList<ConfigurationSetting> ReadConfigurations()
    {
        var configSettings = GetSettingsPart<IConfigurationSettings>();
        if (configSettings is null)
            return Array.Empty<ConfigurationSetting>();

        return configSettings.GetConfigurations()
            .Select(c => new ConfigurationSetting(
                Name: c.Name,
                // IsActive and database fields are not exposed on IConfiguration in 10.21.1.
                IsActive: false,
                DatabaseType: null,
                DatabaseConnectionString: null,
                CustomSettings: (IReadOnlyDictionary<string, string>)c.GetCustomSettings()
                    .ToDictionary(cs => cs.Name, cs => cs.Value)))
            .ToList();
    }

    public bool SetActiveConfiguration(string configurationName)
    {
        // IConfigurationSettings in 10.21.1 has no method to mark a configuration active;
        // active configuration is managed by ILocalRunConfigurationsService (UI service),
        // which is not available on IModel alone. Return false to indicate not supported.
        return false;
    }

    // --- Document exclusion ---

    public bool SetDocumentExcluded(DocumentId document, bool excluded)
    {
        foreach (var module in _model.Root.GetModules())
        {
            var found = FindDocumentWithParent(module, document.Value);
            if (found.Document is not null)
            {
                using var tx = _model.StartTransaction($"Set excluded={excluded} for '{document.QualifiedName}'");
                found.Document.Excluded = excluded;
                tx.Commit();
                return true;
            }
        }
        return false;
    }

    // --- Save / persistence ---

    /// <summary>
    /// No-op: <see cref="IModel"/> persists changes per-transaction via
    /// <c>ITransaction.Commit()</c>; there is no model-wide save in the ExtensionsAPI surface.
    /// </summary>
    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;

    // --- Private helpers ---

    /// <summary>
    /// Converts the ExtensionsAPI string ID (a GUID-formatted string) to a <see cref="Guid"/>.
    /// Falls back to a deterministic name-based Guid if parsing fails.
    /// </summary>
    private static Guid ParseId(string id)
        => Guid.TryParse(id, out var guid) ? guid : GuidFromString(id);

    private static Guid GuidFromString(string s)
    {
        var bytes = new byte[16];
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(s));
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    private IModule ResolveModule(ModuleId moduleId)
    {
        // Try by Id first; fall back to name if Guid roundtrip doesn't match
        var module = _model.Root.GetModules()
            .FirstOrDefault(m => Guid.TryParse(m.Id, out var g) && g == moduleId.Value)
            ?? _model.Root.GetModules()
            .FirstOrDefault(m => string.Equals(m.Name, moduleId.Name, StringComparison.OrdinalIgnoreCase));
        return module ?? throw new InvalidOperationException($"Module '{moduleId.Name}' (id={moduleId.Value}) not found");
    }

    private static IReadOnlyList<DocumentId> CollectModuleDocuments(IModule module, string? typeFilter)
    {
        var result = new List<DocumentId>();
        CollectDocumentsInFolder(module, module.Name, typeFilter, result);
        return result;
    }

    private static void CollectDocumentsInFolder(IFolderBase folder, string moduleName, string? typeFilter, List<DocumentId> result)
    {
        foreach (var doc in folder.GetDocuments())
        {
            if (typeFilter is null || doc.GetType().Name.Contains(typeFilter, StringComparison.OrdinalIgnoreCase))
                result.Add(new DocumentId(ParseId(doc.Id), $"{moduleName}.{doc.Name}"));
        }
        foreach (var subfolder in folder.GetFolders())
            CollectDocumentsInFolder(subfolder, moduleName, typeFilter, result);
    }

    private static IDocument? FindDocumentInFolder(IFolderBase folder, string docName)
    {
        var doc = folder.GetDocuments()
            .FirstOrDefault(d => string.Equals(d.Name, docName, StringComparison.OrdinalIgnoreCase));
        if (doc is not null) return doc;
        foreach (var subfolder in folder.GetFolders())
        {
            var found = FindDocumentInFolder(subfolder, docName);
            if (found is not null) return found;
        }
        return null;
    }

    private static void CollectFolders(IFolderBase parent, string pathPrefix, List<FolderId> result)
    {
        foreach (var folder in parent.GetFolders())
        {
            var folderPath = string.IsNullOrEmpty(pathPrefix) ? folder.Name : $"{pathPrefix}/{folder.Name}";
            result.Add(new FolderId(ParseId(folder.Id), folderPath));
            CollectFolders(folder, folderPath, result);
        }
    }

    private static IFolder? FindFolderByPath(IFolderBase parent, string path)
    {
        // Supports both single-segment "FolderA" and multi-segment "FolderA/FolderB" paths
        var slash = path.IndexOf('/');
        var segment = slash < 0 ? path : path[..slash];
        var rest = slash < 0 ? null : path[(slash + 1)..];

        var folder = parent.GetFolders()
            .FirstOrDefault(f => string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase));
        if (folder is null) return null;
        if (rest is null) return folder;
        return FindFolderByPath(folder, rest);
    }

    private static IFolder? FindFolderById(IFolderBase parent, Guid id)
    {
        foreach (var folder in parent.GetFolders())
        {
            if (ParseId(folder.Id) == id) return folder;
            var found = FindFolderById(folder, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static (IFolder? Folder, IFolderBase Parent) FindFolderWithParent(IFolderBase parent, Guid id)
    {
        foreach (var folder in parent.GetFolders())
        {
            if (ParseId(folder.Id) == id) return (folder, parent);
            var found = FindFolderWithParent(folder, id);
            if (found.Folder is not null) return found;
        }
        return (null, parent);
    }

    private static (IDocument? Document, IFolderBase Parent) FindDocumentWithParent(IFolderBase parent, Guid id)
    {
        foreach (var doc in parent.GetDocuments())
        {
            if (ParseId(doc.Id) == id) return (doc, parent);
        }
        foreach (var folder in parent.GetFolders())
        {
            var found = FindDocumentWithParent(folder, id);
            if (found.Document is not null) return found;
        }
        return (null, parent);
    }

    private T? GetSettingsPart<T>() where T : class, IProjectSettingsPart
    {
        var settings = _model.Root.GetProjectDocuments().OfType<IProjectSettings>().FirstOrDefault();
        return settings?.GetSettingsParts().OfType<T>().FirstOrDefault();
    }
}
