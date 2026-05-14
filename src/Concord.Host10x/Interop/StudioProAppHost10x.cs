namespace Concord.Host10x.Interop;

using Mendix.StudioPro.ExtensionsAPI.Model;
using Terminal.Interop;

/// <summary>
/// 10.x mirror of <c>StudioProAppHost11x</c>. The 10.21.1 ExtensionsAPI
/// surfaces the same <c>IModel.Root</c> / <c>IProject</c> shape we rely on.
/// </summary>
public sealed class StudioProAppHost10x : IStudioProAppHost
{
    private readonly Func<IModel?> getModel;

    public StudioProAppHost10x(Func<IModel?> getModel)
    {
        this.getModel = getModel ?? throw new ArgumentNullException(nameof(getModel));
    }

    public string ProjectPath => getModel()?.Root?.DirectoryPath ?? string.Empty;

    public string ProjectName => getModel()?.Root?.Name ?? string.Empty;

    public bool HasOpenProject
        => getModel()?.Root?.DirectoryPath is { } d && !string.IsNullOrEmpty(d);
}
