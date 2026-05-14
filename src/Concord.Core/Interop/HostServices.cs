using Terminal;

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
    private static IModelHost? _model;
    private static IDomainModelHost? _domainModel;
    private static IPageGenerationHost? _pageGeneration;
    private static INavigationHost? _navigation;
    private static IVersionControlHost? _versionControl;
    private static IUntypedModelHost? _untypedModel;
    private static IMicroflowAuthoringHost? _microflowAuthoring;
    // Late-bound pane-scoped services set by the pane after construction.
    // These are NOT passed to Register because their inputs (CurrentApp,
    // localRunConfigs, user settings, Logger) are only available after the
    // pane opens, not at MEF activation time.
    private static IRunStateProbe? _runStateProbe;
    private static IStudioProUiAutomation? _uiAutomation;
    private static Maia.MaiaActions? _maiaActions;
    private static readonly object _gate = new();

    public static IStudioProAppHost App
        => _app ?? throw NotInitialized(nameof(IStudioProAppHost));
    public static IRunConfigurationsHost RunConfigurations
        => _runConfigs ?? throw NotInitialized(nameof(IRunConfigurationsHost));
    public static IRunStateHost RunState
        => _runState ?? throw NotInitialized(nameof(IRunStateHost));
    public static IModuleImportHost ModuleImport
        => _moduleImport ?? throw NotInitialized(nameof(IModuleImportHost));
    public static IModelHost Model
        => _model ?? throw NotInitialized(nameof(IModelHost));
    public static IDomainModelHost DomainModel
        => _domainModel ?? throw NotInitialized(nameof(IDomainModelHost));
    public static IPageGenerationHost PageGeneration
        => _pageGeneration ?? throw NotInitialized(nameof(IPageGenerationHost));
    public static INavigationHost Navigation
        => _navigation ?? throw NotInitialized(nameof(INavigationHost));
    public static IVersionControlHost VersionControl
        => _versionControl ?? throw NotInitialized(nameof(IVersionControlHost));
    public static IUntypedModelHost UntypedModel
        => _untypedModel ?? throw NotInitialized(nameof(IUntypedModelHost));
    public static IMicroflowAuthoringHost MicroflowAuthoring
        => _microflowAuthoring ?? throw NotInitialized(nameof(IMicroflowAuthoringHost));

    /// <summary>
    /// Set by the pane after constructing RunStateProbe (which needs the pane's
    /// CurrentApp and localRunConfigs closure). Hot-reload on settings save
    /// re-sets this with a fresh instance.
    /// </summary>
    public static IRunStateProbe RunStateProbe
        => _runStateProbe ?? throw NotInitialized(nameof(IRunStateProbe));

    /// <summary>
    /// Set by the pane after constructing StudioProUiAutomation (which needs
    /// hotkeys from user settings and a Logger). Hot-reload on settings save
    /// re-sets this with a fresh instance.
    /// </summary>
    public static IStudioProUiAutomation UiAutomation
        => _uiAutomation ?? throw NotInitialized(nameof(IStudioProUiAutomation));

    /// <summary>
    /// Set by the pane after constructing MaiaActions (pane-scoped, hot-swapped
    /// on settings save). Null when Maia integration is disabled or not yet
    /// initialized. MaiaToolsBootstrap delegates read this at invoke time (late
    /// binding) so hot-swaps are visible without re-registration.
    /// </summary>
    public static Maia.MaiaActions? MaiaActions
    {
        get { lock (_gate) return _maiaActions; }
    }

    public static void SetRunStateProbe(IRunStateProbe probe)
    {
        lock (_gate) { _runStateProbe = probe; }
    }

    public static void SetUiAutomation(IStudioProUiAutomation ui)
    {
        lock (_gate) { _uiAutomation = ui; }
    }

    public static void SetMaiaActions(Maia.MaiaActions? maia)
    {
        lock (_gate) { _maiaActions = maia; }
    }

    // Pane-scoped setters for the 7 model-tier Interop hosts. Production
    // cannot pass these to Register because their constructors require
    // IModel from CurrentApp, which is only available after the pane opens.
    // The pane calls these setters in TryAutoStartActionServer, mirroring
    // the SetRunStateProbe/SetUiAutomation/SetMaiaActions pattern. Tests
    // may still use the 11-arg Register overload with fakes.

    public static void SetModel(IModelHost? model)
    {
        lock (_gate) { _model = model; }
    }

    public static void SetDomainModel(IDomainModelHost? domainModel)
    {
        lock (_gate) { _domainModel = domainModel; }
    }

    public static void SetPageGeneration(IPageGenerationHost? pageGeneration)
    {
        lock (_gate) { _pageGeneration = pageGeneration; }
    }

    public static void SetNavigation(INavigationHost? navigation)
    {
        lock (_gate) { _navigation = navigation; }
    }

    public static void SetVersionControl(IVersionControlHost? versionControl)
    {
        lock (_gate) { _versionControl = versionControl; }
    }

    public static void SetUntypedModel(IUntypedModelHost? untypedModel)
    {
        lock (_gate) { _untypedModel = untypedModel; }
    }

    public static void SetMicroflowAuthoring(IMicroflowAuthoringHost? microflowAuthoring)
    {
        lock (_gate) { _microflowAuthoring = microflowAuthoring; }
    }

    /// <summary>
    /// Legacy 4-argument overload retained for backward compatibility.
    /// New accessors (Model … MicroflowAuthoring) will throw until the
    /// 11-argument overload is called.
    /// </summary>
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

    /// <summary>
    /// Full 11-argument overload that registers all Core Interop services at once.
    /// Host DLLs should prefer this overload; the 4-argument overload is kept for
    /// test compatibility and incremental adoption.
    /// </summary>
    public static void Register(
        IStudioProAppHost app,
        IRunConfigurationsHost runConfigs,
        IRunStateHost runState,
        IModuleImportHost moduleImport,
        IModelHost model,
        IDomainModelHost domainModel,
        IPageGenerationHost pageGeneration,
        INavigationHost navigation,
        IVersionControlHost versionControl,
        IUntypedModelHost untypedModel,
        IMicroflowAuthoringHost microflowAuthoring)
    {
        lock (_gate)
        {
            _app = app;
            _runConfigs = runConfigs;
            _runState = runState;
            _moduleImport = moduleImport;
            _model = model;
            _domainModel = domainModel;
            _pageGeneration = pageGeneration;
            _navigation = navigation;
            _versionControl = versionControl;
            _untypedModel = untypedModel;
            _microflowAuthoring = microflowAuthoring;
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
            _model = null;
            _domainModel = null;
            _pageGeneration = null;
            _navigation = null;
            _versionControl = null;
            _untypedModel = null;
            _microflowAuthoring = null;
            _runStateProbe = null;
            _uiAutomation = null;
            _maiaActions = null;
        }
    }

    private static InvalidOperationException NotInitialized(string serviceName)
        => new($"HostServices.{serviceName} was accessed before HostServices.Register was called. " +
               "Each host DLL must call Register from its MEF activation.");
}
