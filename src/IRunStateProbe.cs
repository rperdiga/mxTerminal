namespace Terminal;

public enum RunState { Unknown, Running, Stopped }

public interface IRunStateProbe
{
    /// <summary>Last-known absolute URL, e.g. <c>http://localhost:8080</c>, or null if no active config.</summary>
    string? GetActiveUrl();

    /// <summary>Port parsed out of <see cref="GetActiveUrl"/>, or null if absent/unparseable.</summary>
    int? GetActivePort();

    /// <summary>Probe the runtime port via TCP connect. Result reflects current observable state.</summary>
    Task<RunState> IsRunningAsync(CancellationToken ct = default);
}
