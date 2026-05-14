namespace Concord.Host11x.Interop;

using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.Settings;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Terminal.Interop;

/// <summary>
/// Wraps <c>ILocalRunConfigurationsService</c> (active configuration) and
/// <c>IConfigurationSettings</c> (the list of configurations on the project
/// settings) for Core. Studio Pro's <see cref="IConfiguration"/> exposes
/// <c>Name</c> + <c>ApplicationRootUrl</c> only — no separate Id — so the
/// configuration name doubles as the identifier in
/// <see cref="RunConfigurationInfo"/>.
/// </summary>
public sealed class RunConfigurationsHost11x : IRunConfigurationsHost
{
    private readonly Func<IModel?> getModel;
    private readonly ILocalRunConfigurationsService? service;

    /// <param name="service">
    /// MEF-imported <c>ILocalRunConfigurationsService</c>. <c>null</c> is
    /// accepted so <see cref="Concord.Host11x.Host11xEntry"/> can register a
    /// placeholder at MEF activation (before the pane has resolved the
    /// service); the pane then swaps in a fully-wired instance via
    /// <see cref="Terminal.Interop.HostServices.SetRunConfigurations"/> in
    /// <c>TryAutoStartActionServer</c>. <see cref="GetActive"/> returns
    /// <c>null</c> while the service is unset, which is the same shape
    /// callers handle when no project is open.
    /// </param>
    public RunConfigurationsHost11x(Func<IModel?> getModel, ILocalRunConfigurationsService? service)
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
