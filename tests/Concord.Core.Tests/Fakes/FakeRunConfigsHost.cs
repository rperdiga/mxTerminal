namespace Concord.Core.Tests.Fakes;

using Terminal.Interop;

public sealed class FakeRunConfigsHost : IRunConfigurationsHost
{
    public RunConfigurationInfo? GetActive() => throw new NotImplementedException();
    public IReadOnlyList<RunConfigurationInfo> ListAll() => throw new NotImplementedException();
}
