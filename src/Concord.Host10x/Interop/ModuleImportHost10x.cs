namespace Concord.Host10x.Interop;

using Terminal.Interop;

public class ModuleImportHost10x : IModuleImportHost
{
    public bool IsModuleImported(string moduleName)
        => throw new NotImplementedException("Pending Task 15 + W4 module import scope");
    public ModuleImportResult ImportFromMpk(string mpkAbsolutePath)
        => throw new NotImplementedException("Pending Task 15 + W4 module import scope");
}
