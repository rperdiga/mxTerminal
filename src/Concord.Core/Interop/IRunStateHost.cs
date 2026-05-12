namespace Terminal.Interop;

public enum AppRunState { Stopped, Starting, Running, Stopping, Unknown }

public interface IRunStateHost
{
    AppRunState GetCurrentState();
}
