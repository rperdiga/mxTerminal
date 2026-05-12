namespace Concord.Host10x.Interop;

using Terminal.Interop;

public class RunStateHost10x : IRunStateHost
{
    public AppRunState GetCurrentState()
        => throw new NotImplementedException("Pending Task 15 — 10.x RunStateProbe implementation");
}
