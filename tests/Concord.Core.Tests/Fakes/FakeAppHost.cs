namespace Concord.Core.Tests.Fakes;

using Terminal.Interop;

public sealed class FakeAppHost : IStudioProAppHost
{
    public string ProjectPath => throw new NotImplementedException();
    public string ProjectName => throw new NotImplementedException();
    public bool HasOpenProject => throw new NotImplementedException();
}
