namespace Concord.Core.Tests.Fakes;

using Terminal.Interop;

public sealed class FakeRunStateHost : IRunStateHost
{
    public AppRunState GetCurrentState() => throw new NotImplementedException();
}
