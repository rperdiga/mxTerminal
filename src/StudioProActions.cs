namespace Terminal;

/// <summary>
/// Result of one action call. Exactly one of <see cref="Status"/> or
/// <see cref="Error"/> is non-null.
/// </summary>
public sealed record ActionResult(string? Status = null, string? Url = null, string? Error = null)
{
    public static ActionResult Ok(string status, string? url = null) => new(Status: status, Url: url);
    public static ActionResult Fail(string error) => new(Error: error);
}

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

    public StudioProActions(
        IRunStateProbe probe,
        IStudioProUiAutomation ui,
        TimeSpan? runTimeout = null,
        TimeSpan? stopTimeout = null,
        TimeSpan? pollInterval = null)
    {
        this.probe = probe;
        this.ui = ui;
        this.runTimeout = runTimeout ?? DefaultRunTimeout;
        this.stopTimeout = stopTimeout ?? DefaultStopTimeout;
        this.pollInterval = pollInterval ?? DefaultPollInterval;
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
                return ActionResult.Fail("Studio Pro main window unavailable; try again after the IDE finishes loading");

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
                return ActionResult.Fail("Studio Pro main window unavailable; try again after the IDE finishes loading");

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
                return ActionResult.Fail("Studio Pro main window unavailable; try again after the IDE finishes loading");
            return ActionResult.Ok("reloaded");
        }
        finally { gate.Release(); }
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
