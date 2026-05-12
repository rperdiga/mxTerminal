namespace Terminal.Interop;

/// <summary>
/// Registry of Studio-Pro-typed implementations supplied by the active host DLL.
/// Each host's MEF entry calls Register at startup, then Core resolves via the
/// public getters.
/// </summary>
public static class HostServices
{
    private static IStudioProAppHost? _app;
    private static IRunConfigurationsHost? _runConfigs;
    private static IRunStateHost? _runState;
    private static IModuleImportHost? _moduleImport;
    private static readonly object _gate = new();

    public static IStudioProAppHost App
        => _app ?? throw NotInitialized(nameof(IStudioProAppHost));
    public static IRunConfigurationsHost RunConfigurations
        => _runConfigs ?? throw NotInitialized(nameof(IRunConfigurationsHost));
    public static IRunStateHost RunState
        => _runState ?? throw NotInitialized(nameof(IRunStateHost));
    public static IModuleImportHost ModuleImport
        => _moduleImport ?? throw NotInitialized(nameof(IModuleImportHost));

    public static void Register(
        IStudioProAppHost app,
        IRunConfigurationsHost runConfigs,
        IRunStateHost runState,
        IModuleImportHost moduleImport)
    {
        lock (_gate)
        {
            _app = app;
            _runConfigs = runConfigs;
            _runState = runState;
            _moduleImport = moduleImport;
        }
    }

    internal static void Reset()
    {
        lock (_gate)
        {
            _app = null;
            _runConfigs = null;
            _runState = null;
            _moduleImport = null;
        }
    }

    private static InvalidOperationException NotInitialized(string serviceName)
        => new($"HostServices.{serviceName} was accessed before HostServices.Register was called. " +
               "Each host DLL must call Register from its MEF activation.");
}
