namespace Concord.Host10x.Interop;

using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.Settings;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Terminal.Interop;

/// <summary>
/// 10.x mirror of <c>RunConfigurationsHost11x</c>. Studio Pro 10.21.1
/// exposes the same <c>ILocalRunConfigurationsService.GetActiveConfiguration</c>
/// and <c>IConfigurationSettings.GetConfigurations</c> surface, so the 10.x
/// and 11.x hosts use identical adapter code modulo namespace.
/// </summary>
public sealed class RunConfigurationsHost10x : IRunConfigurationsHost
{
    private readonly Func<IModel?> getModel;
    private readonly ILocalRunConfigurationsService? service;

    /// <param name="service">
    /// MEF-imported <c>ILocalRunConfigurationsService</c>. <c>null</c> is
    /// accepted so <see cref="Concord.Host10x.Host10xEntry"/> can register a
    /// placeholder at MEF activation (before the pane has resolved the
    /// service); the pane then swaps in a fully-wired instance via
    /// <see cref="Terminal.Interop.HostServices.SetRunConfigurations"/> in
    /// <c>TryAutoStartActionServer</c>.
    /// </param>
    public RunConfigurationsHost10x(Func<IModel?> getModel, ILocalRunConfigurationsService? service)
    {
        this.getModel = getModel ?? throw new ArgumentNullException(nameof(getModel));
        this.service = service;
    }

    public RunConfigurationInfo? GetActive()
    {
        if (service is null) return null;
        var model = getModel();
        if (model is null) return null;
        var cfg = service.GetActiveConfiguration(model);
        if (cfg is null) return null;
        return new RunConfigurationInfo(cfg.Name, cfg.Name, cfg.ApplicationRootUrl);
    }

    public IReadOnlyList<RunConfigurationInfo> ListAll()
    {
        var model = getModel();
        if (model is null) return Array.Empty<RunConfigurationInfo>();
        var settings = model.Root?.GetProjectDocuments()
            .OfType<IProjectSettings>()
            .FirstOrDefault();
        var configSettings = settings?.GetSettingsParts()
            .OfType<IConfigurationSettings>()
            .FirstOrDefault();
        if (configSettings is null) return Array.Empty<RunConfigurationInfo>();
        return configSettings.GetConfigurations()
            .Select(c => new RunConfigurationInfo(c.Name, c.Name, c.ApplicationRootUrl))
            .ToList();
    }
}
