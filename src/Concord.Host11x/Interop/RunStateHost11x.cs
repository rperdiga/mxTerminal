namespace Concord.Host11x.Interop;

using Terminal.Interop;

public class RunStateHost11x : IRunStateHost
{
    public AppRunState GetCurrentState()
        => throw new NotImplementedException("Task 15 wires this to Concord.Host11x.Interop.RunStateProbe");
}
