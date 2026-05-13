using FluentAssertions;
using Terminal;
using Terminal.Interop;
using Xunit;

namespace Terminal.Tests;

public class StudioProActionsTests : IDisposable
{
    private sealed class FakeProbe : IRunStateProbe
    {
        public Queue<RunState> States { get; } = new();
        public string? Url { get; set; } = "http://localhost:8080";
        public int? Port { get; set; } = 8080;
        public string? GetActiveUrl() => Url;
        public int? GetActivePort() => Port;
        public Task<RunState> IsRunningAsync(CancellationToken ct = default) =>
            Task.FromResult(States.Count > 0 ? States.Dequeue() : RunState.Unknown);
    }

    private sealed class FakeUi : IStudioProUiAutomation
    {
        public int RunCount, StopCount, RefreshCount, SaveAllCount;
        public bool RunOk = true, StopOk = true, RefreshOk = true, SaveAllOk = true;
        public string? FailureReason;
        public bool TriggerRun()             { RunCount++;     return RunOk; }
        public bool TriggerStop()            { StopCount++;    return StopOk; }
        public bool TriggerRefreshFromDisk() { RefreshCount++; return RefreshOk; }
        public bool TriggerSaveAll()         { SaveAllCount++; return SaveAllOk; }
        public string? LastFailureReason => FailureReason;
    }

    private static StudioProActions NewActions(FakeProbe probe, FakeUi ui)
    {
        HostServices.SetRunStateProbe(probe);
        HostServices.SetUiAutomation(ui);
        return new StudioProActions(
            runTimeout: TimeSpan.FromMilliseconds(500),
            stopTimeout: TimeSpan.FromMilliseconds(500),
            pollInterval: TimeSpan.FromMilliseconds(50));
    }

    public void Dispose() { /* HostServices state is re-set per-call in NewActions */ }

    [Fact]
    public async Task RunApp_AlreadyRunning_ReturnsAlreadyRunning_DoesNotTrigger()
    {
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Running);
        var ui = new FakeUi();
        var result = await NewActions(probe, ui).RunAppAsync();

        result.Status.Should().Be("already_running");
        result.Url.Should().Be("http://localhost:8080");
        ui.RunCount.Should().Be(0);
    }

    [Fact]
    public async Task RunApp_Stopped_TriggersAndWaitsForRunning()
    {
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Stopped);   // before
        probe.States.Enqueue(RunState.Stopped);   // first poll — still starting
        probe.States.Enqueue(RunState.Running);   // second poll — up
        var ui = new FakeUi();

        var result = await NewActions(probe, ui).RunAppAsync();

        result.Status.Should().Be("started");
        result.Url.Should().Be("http://localhost:8080");
        ui.RunCount.Should().Be(1);
    }

    [Fact]
    public async Task RunApp_TimesOut_ReturnsCommandSent()
    {
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Stopped);
        // No subsequent enqueues; FakeProbe returns Unknown forever after.
        var ui = new FakeUi();

        var result = await NewActions(probe, ui).RunAppAsync();

        result.Status.Should().Be("command_sent");
        ui.RunCount.Should().Be(1);
    }

    [Fact]
    public async Task RunApp_ProbeUnknownBefore_ReturnsCommandSent()
    {
        var probe = new FakeProbe();   // empty -> Unknown
        var ui = new FakeUi();

        var result = await NewActions(probe, ui).RunAppAsync();

        result.Status.Should().Be("command_sent");
        ui.RunCount.Should().Be(1);
    }

    [Fact]
    public async Task RunApp_HwndZero_ReturnsError()
    {
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Stopped);
        var ui = new FakeUi { RunOk = false };

        var result = await NewActions(probe, ui).RunAppAsync();

        result.Error.Should().Contain("main window unavailable");
    }

    [Fact]
    public async Task StopApp_NotRunning_ReturnsWasntRunning()
    {
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Stopped);
        var ui = new FakeUi();
        var result = await NewActions(probe, ui).StopAppAsync();
        result.Status.Should().Be("wasnt_running");
        ui.StopCount.Should().Be(0);
    }

    [Fact]
    public async Task StopApp_Running_TriggersAndWaitsForStopped()
    {
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Running);   // before
        probe.States.Enqueue(RunState.Running);   // first poll
        probe.States.Enqueue(RunState.Stopped);   // second poll
        var ui = new FakeUi();
        var result = await NewActions(probe, ui).StopAppAsync();
        result.Status.Should().Be("stopped");
        ui.StopCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshProject_ReturnsReloaded_OnSuccess()
    {
        var probe = new FakeProbe();
        var ui = new FakeUi();
        var result = await NewActions(probe, ui).RefreshProjectAsync();
        result.Status.Should().Be("reloaded");
        ui.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshProject_HwndZero_ReturnsError()
    {
        var probe = new FakeProbe();
        var ui = new FakeUi { RefreshOk = false };
        var result = await NewActions(probe, ui).RefreshProjectAsync();
        result.Error.Should().Contain("main window unavailable");
    }

    [Fact]
    public async Task RunApp_TriggerFails_PropagatesUiFailureReason()
    {
        // When the UI automation layer surfaces a specific reason (e.g. macOS
        // "Accessibility permission not granted"), the action layer must pass
        // it through so MCP clients can guide the user — instead of always
        // showing the generic "main window unavailable" text.
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Stopped);
        var ui = new FakeUi
        {
            RunOk = false,
            FailureReason = "macOS Accessibility permission not granted to Studio Pro. Open System Settings → Privacy & Security → Accessibility.",
        };

        var result = await NewActions(probe, ui).RunAppAsync();

        result.Error.Should().Contain("Accessibility permission");
        result.Error.Should().NotContain("main window unavailable");
    }

    [Fact]
    public async Task ConcurrentCalls_AreSerialized()
    {
        var probe = new FakeProbe();
        // Both calls see Running before, so both return already_running.
        probe.States.Enqueue(RunState.Running);
        probe.States.Enqueue(RunState.Running);
        var ui = new FakeUi();
        var actions = NewActions(probe, ui);

        var t1 = actions.RunAppAsync();
        var t2 = actions.RunAppAsync();
        await Task.WhenAll(t1, t2);
        t1.Result.Status.Should().Be("already_running");
        t2.Result.Status.Should().Be("already_running");
    }
}
