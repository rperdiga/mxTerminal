namespace Terminal.Interop;

public record RunConfigurationInfo(string Id, string Name, string? ApplicationRootUrl);

public interface IRunConfigurationsHost
{
    RunConfigurationInfo? GetActive();
    IReadOnlyList<RunConfigurationInfo> ListAll();
}
