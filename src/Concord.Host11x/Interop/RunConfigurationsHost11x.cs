namespace Concord.Host11x.Interop;

using Terminal.Interop;

public class RunConfigurationsHost11x : IRunConfigurationsHost
{
    public RunConfigurationInfo? GetActive()
        => throw new NotImplementedException("Task 15 wires this to ILocalRunConfigurationsService");
    public IReadOnlyList<RunConfigurationInfo> ListAll()
        => throw new NotImplementedException("Task 15 wires this");
}
