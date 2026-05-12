namespace Terminal.Interop;

public record ModuleImportResult(bool Success, string? Error);

public interface IModuleImportHost
{
    bool IsModuleImported(string moduleName);
    ModuleImportResult ImportFromMpk(string mpkAbsolutePath);
}
