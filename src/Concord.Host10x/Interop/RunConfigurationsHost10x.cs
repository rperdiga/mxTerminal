namespace Concord.Host10x.Interop;

using Terminal.Interop;

public class RunConfigurationsHost10x : IRunConfigurationsHost
{
    public RunConfigurationInfo? GetActive()
        => throw new NotImplementedException("Pending Task 15 — 10.x ILocalRunConfigurationsService surface verification");
    public IReadOnlyList<RunConfigurationInfo> ListAll()
        => throw new NotImplementedException("Pending Task 15");
}
