namespace MxStudioProTerminal;

/// <summary>
/// Factory abstraction so SessionManager can be unit-tested without spawning processes.
/// Single method: spawn a PTY and return the wrapped session.
/// </summary>
public interface IPtyFactory
{
    /// <summary>
    /// Spawn a PTY subprocess and return a session handle.
    /// </summary>
    /// <param name="shellPath">Path (or name) of the executable to run.</param>
    /// <param name="args">Arguments to pass to the executable.</param>
    /// <param name="cwd">Working directory for the spawned process.</param>
    /// <param name="cols">Initial terminal width in columns.</param>
    /// <param name="rows">Initial terminal height in rows.</param>
    /// <param name="environment">
    /// Additional or override environment variables.
    /// NOTE: Pty.Net 0.1.16-pre does not support custom environment injection;
    /// this parameter is accepted for interface compatibility and ignored in the real implementation.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<IPtySession> SpawnAsync(
        string shellPath,
        string[] args,
        string cwd,
        int cols,
        int rows,
        IDictionary<string, string> environment,
        CancellationToken ct);
}

/// <summary>
/// Represents a live PTY session wrapping an underlying process.
/// </summary>
public interface IPtySession : IDisposable
{
    /// <summary>
    /// Process ID of the spawned process, or -1 if not available from the backend.
    /// </summary>
    int Pid { get; }

    /// <summary>
    /// Write raw bytes to the PTY's input stream. Bytes are interpreted as UTF-8.
    /// </summary>
    Task WriteAsync(byte[] data, CancellationToken ct);

    /// <summary>
    /// Read the next available chunk of output from the PTY.
    /// Returns the number of bytes written into <paramref name="buffer"/>.
    /// Returns 0 when the PTY has disconnected and no more data is available.
    /// </summary>
    Task<int> ReadAsync(byte[] buffer, CancellationToken ct);

    /// <summary>Resize the PTY window.</summary>
    void Resize(int cols, int rows);

    /// <summary>
    /// Exit code of the process, or <c>null</c> if it has not yet exited
    /// or if the backend does not expose the exit code.
    /// </summary>
    int? ExitCode { get; }

    /// <summary>
    /// Raised when the PTY process has exited. The argument carries the exit code,
    /// or <c>null</c> if the backend does not expose it.
    /// </summary>
    event EventHandler<int?> Exited;
}
