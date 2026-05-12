namespace Concord.Core.Tests.Fakes;

using Terminal.Interop;

public sealed class FakeVersionControlHost : IVersionControlHost
{
    public bool IsAvailable => false;
    public VersionControlInfo Read() => throw new NotImplementedException();
}
