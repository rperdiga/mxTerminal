namespace Concord.Host11x.Interop;

using Terminal.Interop;

public class ModuleImportHost11x : IModuleImportHost
{
    public bool IsModuleImported(string moduleName)
        => throw new NotImplementedException("Task 15 wires this to IApp.Root.Modules");
    public ModuleImportResult ImportFromMpk(string mpkAbsolutePath)
        => throw new NotImplementedException("Task 15 wires this (or W4 if module import is W4 scope)");
}
