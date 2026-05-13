namespace Concord.Core.Tests.Fakes;

using Terminal;

public sealed class FakeUiAutomation : IStudioProUiAutomation
{
    public bool TriggerRun() => true;
    public bool TriggerStop() => true;
    public bool TriggerRefreshFromDisk() => true;
    public bool TriggerSaveAll() => true;
    public string? LastFailureReason => null;
}
