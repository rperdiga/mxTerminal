namespace Concord.Host11x.Interop;

using Mendix.StudioPro.ExtensionsAPI.Model;
using Terminal.Interop;

/// <summary>
/// Wraps Studio Pro's <c>IModel.Root</c> (an <c>IProject</c>) so Core can
/// read the open project's directory and display name without depending on
/// the Mendix ExtensionsAPI types. The <see cref="IModel"/> is supplied by
/// the pane via a closure because it's only available after the pane opens,
/// not at MEF activation time.
/// </summary>
public sealed class StudioProAppHost11x : IStudioProAppHost
{
    private readonly Func<IModel?> getModel;

    public StudioProAppHost11x(Func<IModel?> getModel)
    {
        this.getModel = getModel ?? throw new ArgumentNullException(nameof(getModel));
    }

    public string ProjectPath => getModel()?.Root?.DirectoryPath ?? string.Empty;

    public string ProjectName => getModel()?.Root?.Name ?? string.Empty;

    public bool HasOpenProject
        => getModel()?.Root?.DirectoryPath is { } d && !string.IsNullOrEmpty(d);
}
