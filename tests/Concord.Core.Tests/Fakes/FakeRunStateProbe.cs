namespace Concord.Core.Tests.Fakes;

using Terminal;

public sealed class FakeRunStateProbe : IRunStateProbe
{
    public string? GetActiveUrl() => null;
    public int? GetActivePort() => null;
    public Task<RunState> IsRunningAsync(CancellationToken ct = default) => Task.FromResult(RunState.Stopped);
}
