namespace Concord.Core.Tests.Fakes;

using System.Threading;
using System.Threading.Tasks;
using Terminal.Interop;

public sealed class FakeModelHost : IModelHost
{
    public ProjectInfo GetProjectInfo() => throw new NotImplementedException();
    public IReadOnlyList<ModuleId> ListModules() => throw new NotImplementedException();
    public ModuleId? GetModuleByName(string moduleName) => throw new NotImplementedException();
    public IReadOnlyList<DocumentId> ListModuleDocuments(ModuleId moduleId, string? documentTypeFilter = null) => throw new NotImplementedException();
    public IReadOnlyList<DocumentId> ListAllDocuments(string? documentTypeFilter = null) => throw new NotImplementedException();
    public DocumentId? GetDocumentByQualifiedName(string qualifiedName) => throw new NotImplementedException();
    public IReadOnlyList<FolderId> ListFolders(ModuleId moduleId) => throw new NotImplementedException();
    public FolderId? CreateFolder(ModuleId moduleId, string parentFolderPath, string folderName) => throw new NotImplementedException();
    public bool DeleteFolder(FolderId folder) => throw new NotImplementedException();
    public bool MoveDocument(DocumentId document, FolderId? newFolder) => throw new NotImplementedException();
    public IReadOnlyList<RuntimeSetting> ReadRuntimeSettings() => throw new NotImplementedException();
    public bool WriteRuntimeSetting(string key, string? value) => throw new NotImplementedException();
    public IReadOnlyList<ConfigurationSetting> ReadConfigurations() => throw new NotImplementedException();
    public bool SetActiveConfiguration(string configurationName) => throw new NotImplementedException();
    public bool SetDocumentExcluded(DocumentId document, bool excluded) => throw new NotImplementedException();
    public Task SaveAsync(CancellationToken ct = default) => throw new NotImplementedException();
}
