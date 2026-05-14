namespace Concord.Core.Tests.Fakes;

using Terminal.Interop;

public sealed class FakeModuleImportHost : IModuleImportHost
{
    public bool IsModuleImported(string moduleName) => throw new NotImplementedException();
    public ModuleImportResult ImportFromMpk(string mpkAbsolutePath) => throw new NotImplementedException();
}
