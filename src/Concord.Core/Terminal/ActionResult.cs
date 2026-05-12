namespace Terminal;

/// <summary>
/// Result of one action call. <see cref="Error"/> set means failure;
/// otherwise <see cref="Status"/> describes the outcome and any of the
/// optional payload fields may carry richer detail.
/// </summary>
public sealed record ActionResult(
    string? Status = null,
    string? Url = null,
    string? Error = null,
    object? Data = null)
{
    public static ActionResult Ok(string status, string? url = null) => new(Status: status, Url: url);
    public static ActionResult OkWith(string status, object data) => new(Status: status, Data: data);
    public static ActionResult Fail(string error) => new(Error: error);
}

/// <summary>Snapshot of a Mendix local run configuration.</summary>
public sealed record RunConfigurationSnapshot(string? Id, string? Name, string? ApplicationRootUrl);

/// <summary>Composite status for the get_app_status tool.</summary>
public sealed record AppStatusInfo(
    string? ProjectPath,
    string? ProjectName,
    string Running,                    // "running" | "stopped" | "unknown"
    string? RunningUrl,
    RunConfigurationSnapshot? ActiveRunConfiguration);
