namespace Concord.Host11x.Interop;

using Terminal.Interop;

public class StudioProAppHost11x : IStudioProAppHost
{
    public string ProjectPath => throw new NotImplementedException("Task 15 wires this to IApp.Root.DirectoryPath");
    public string ProjectName => throw new NotImplementedException("Task 15 wires this");
    public bool HasOpenProject => throw new NotImplementedException("Task 15 wires this");
}
