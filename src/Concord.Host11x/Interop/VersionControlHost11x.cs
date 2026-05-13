namespace Concord.Host11x.Interop;

using Mendix.StudioPro.ExtensionsAPI.Model;
using Terminal.Interop;

/// <summary>
/// Implements IVersionControlHost against the 11.6.2 ExtensionsAPI surface.
/// Body ported from MendixAdditionalTools.ReadVersionControl (SPMCP source),
/// with JSON parsing stripped and Mendix modeling work retained.
///
/// IVersionControlService lives in Mendix.StudioPro.ExtensionsAPI.UI.Services
/// (NOT in Services) on both 10.21.1 and 11.6.2. The service is resolved via
/// optional constructor injection; IsAvailable reflects whether the injected
/// instance is non-null.
/// </summary>
public sealed class VersionControlHost11x : IVersionControlHost
{
    private readonly IModel _model;
    private readonly Mendix.StudioPro.ExtensionsAPI.UI.Services.IVersionControlService? _vcs;

    public VersionControlHost11x(
        IModel model,
        Mendix.StudioPro.ExtensionsAPI.UI.Services.IVersionControlService? versionControl = null)
    {
        _model = model;
        _vcs = versionControl;
    }

    // ── IVersionControlHost ──────────────────────────────────────────────────

    public bool IsAvailable => _vcs is not null;

    public VersionControlInfo Read()
    {
        if (_vcs is null)
            throw new InvalidOperationException(
                "IVersionControlService is not available. " +
                "IsAvailable is false — check Studio Pro version and DI registration.");

        var isVc = _vcs.IsProjectVersionControlled(_model);
        if (!isVc)
            return new VersionControlInfo(
                IsVersionControlled: false,
                BranchName: null,
                CommitId: null,
                CommitAuthor: null,
                CommitDate: null,
                CommitMessage: null);

        string? branchName = null;
        string? commitId = null;
        string? commitAuthor = null;
        string? commitDate = null;
        string? commitMessage = null;

        try
        {
            var branch = _vcs.GetCurrentBranch(_model);
            branchName = branch?.Name;

            if (branch is not null)
            {
                try
                {
                    var headCommit = _vcs.GetHeadCommit(_model, branch);
                    commitId = headCommit?.ID;
                    commitAuthor = headCommit?.Author;
                    commitDate = headCommit?.Date;
                    commitMessage = headCommit?.Message;
                }
                catch { /* Branch may have no commits */ }
            }
        }
        catch { /* Git config may not be readable */ }

        return new VersionControlInfo(
            IsVersionControlled: true,
            BranchName: branchName,
            CommitId: commitId,
            CommitAuthor: commitAuthor,
            CommitDate: commitDate,
            CommitMessage: commitMessage);
    }
}
