namespace Terminal;

/// <summary>
/// State machine for run_app / stop_app / refresh_project. Pure logic — no
/// HTTP, no DllImports. Acquires a single semaphore so only one action runs at a time.
/// </summary>
public sealed class StudioProActions
{
    private static readonly TimeSpan DefaultRunTimeout    = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultStopTimeout   = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultPollInterval  = TimeSpan.FromMilliseconds(500);

    private readonly IRunStateProbe probe;
    private readonly IStudioProUiAutomation ui;
    private readonly TimeSpan runTimeout;
    private readonly TimeSpan stopTimeout;
    private readonly TimeSpan pollInterval;
    private readonly SemaphoreSlim gate = new(1, 1);
    // Optional callbacks supplied by the pane extension. Decoupled from the
    // Mendix Extensibility API surface so this class stays unit-testable.
    private readonly Func<RunConfigurationSnapshot?>? getActiveRunConfig;
    private readonly Func<(string? path, string? name)>? getProjectInfo;

    public StudioProActions(
        IRunStateProbe probe,
        IStudioProUiAutomation ui,
        TimeSpan? runTimeout = null,
        TimeSpan? stopTimeout = null,
        TimeSpan? pollInterval = null,
        Func<RunConfigurationSnapshot?>? getActiveRunConfig = null,
        Func<(string? path, string? name)>? getProjectInfo = null)
    {
        this.probe = probe;
        this.ui = ui;
        this.runTimeout = runTimeout ?? DefaultRunTimeout;
        this.stopTimeout = stopTimeout ?? DefaultStopTimeout;
        this.pollInterval = pollInterval ?? DefaultPollInterval;
        this.getActiveRunConfig = getActiveRunConfig;
        this.getProjectInfo = getProjectInfo;
    }

    public async Task<ActionResult> RunAppAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before = await probe.IsRunningAsync(ct);
            if (before == RunState.Running)
                return ActionResult.Ok("already_running", probe.GetActiveUrl());

            if (!ui.TriggerRun())
                return ActionResult.Fail(ui.LastFailureReason ?? "Studio Pro main window unavailable; try again after the IDE finishes loading");

            var after = await WaitForAsync(RunState.Running, runTimeout, ct);
            return after switch
            {
                RunState.Running => ActionResult.Ok("started", probe.GetActiveUrl()),
                _                => ActionResult.Ok("command_sent"),
            };
        }
        finally { gate.Release(); }
    }

    public async Task<ActionResult> StopAppAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before = await probe.IsRunningAsync(ct);
            if (before == RunState.Stopped)
                return ActionResult.Ok("wasnt_running");

            if (!ui.TriggerStop())
                return ActionResult.Fail(ui.LastFailureReason ?? "Studio Pro main window unavailable; try again after the IDE finishes loading");

            var after = await WaitForAsync(RunState.Stopped, stopTimeout, ct);
            return after switch
            {
                RunState.Stopped => ActionResult.Ok("stopped"),
                _                => ActionResult.Ok("command_sent"),
            };
        }
        finally { gate.Release(); }
    }

    public async Task<ActionResult> RefreshProjectAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!ui.TriggerRefreshFromDisk())
                return ActionResult.Fail(ui.LastFailureReason ?? "Studio Pro main window unavailable; try again after the IDE finishes loading");
            return ActionResult.Ok("reloaded");
        }
        finally { gate.Release(); }
    }

    /// <summary>Send Ctrl+S to Studio Pro — saves all unsaved model changes.</summary>
    public async Task<ActionResult> SaveAllAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!ui.TriggerSaveAll())
                return ActionResult.Fail(ui.LastFailureReason ?? "Studio Pro main window unavailable; try again after the IDE finishes loading");
            return ActionResult.Ok("save_command_sent");
        }
        finally { gate.Release(); }
    }

    /// <summary>Read-only: returns the currently selected local run configuration.</summary>
    public Task<ActionResult> GetActiveRunConfigurationAsync(CancellationToken ct = default)
    {
        if (getActiveRunConfig is null)
            return Task.FromResult(ActionResult.Fail("Active-run-configuration callback not wired"));
        var cfg = getActiveRunConfig();
        if (cfg is null)
            return Task.FromResult(ActionResult.OkWith("no_active_configuration", new { }));
        return Task.FromResult(ActionResult.OkWith("ok", cfg));
    }

    /// <summary>Composite snapshot for orienting Claude Code: project, run state, active config.</summary>
    public async Task<ActionResult> GetAppStatusAsync(CancellationToken ct = default)
    {
        var (projPath, projName) = getProjectInfo?.Invoke() ?? (null, null);
        var state = await probe.IsRunningAsync(ct);
        var stateStr = state switch
        {
            RunState.Running => "running",
            RunState.Stopped => "stopped",
            _                => "unknown",
        };
        var url = state == RunState.Running ? probe.GetActiveUrl() : null;
        var cfg = getActiveRunConfig?.Invoke();
        var info = new AppStatusInfo(
            ProjectPath: projPath,
            ProjectName: projName,
            Running: stateStr,
            RunningUrl: url,
            ActiveRunConfiguration: cfg);
        return ActionResult.OkWith("ok", info);
    }

    private async Task<RunState> WaitForAsync(RunState target, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var s = await probe.IsRunningAsync(ct);
            if (s == target) return s;
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(pollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
        return await probe.IsRunningAsync(ct);
    }
}
