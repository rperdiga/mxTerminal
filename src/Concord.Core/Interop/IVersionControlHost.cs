namespace Terminal.Interop;

/// <summary>
/// Snapshot of the current version-control state of a Studio Pro project.
/// Maps to the data that ReadVersionControl collects from IVersionControlService:
/// IsVersionControlled, branch name, head commit ID/author/date/message.
/// </summary>
public record VersionControlInfo(
    bool IsVersionControlled,
    string? BranchName,
    string? CommitId,
    string? CommitAuthor,
    string? CommitDate,
    string? CommitMessage);

/// <summary>
/// Wraps Mendix IVersionControlService.
///
/// This service is not available on Studio Pro 10.21.1 (it ships with a later
/// Studio Pro release). IsAvailable MUST be checked before calling Read();
/// the Host10x implementation returns false when the underlying service is null.
///
/// Methods surface:
///   IsProjectVersionControlled  → IsAvailable + Read().IsVersionControlled
///   GetCurrentBranch            → Read().BranchName
///   GetHeadCommit               → Read().CommitId / Author / Date / Message
/// </summary>
public interface IVersionControlHost
{
    /// <summary>
    /// True when the underlying IVersionControlService is present in this Studio Pro version.
    /// False on Studio Pro 10.21.1 and any other version that does not ship the service.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Read the current version-control state of the open project.
    /// Returns a record whose IsVersionControlled field is false when the project
    /// is not under version control (even if the service itself is available).
    /// Throws InvalidOperationException if IsAvailable is false.
    /// </summary>
    VersionControlInfo Read();
}
