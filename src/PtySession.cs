using Pty.Net;

namespace MxStudioProTerminal;

public sealed class PtyNetFactory : IPtyFactory
{
    public async Task<IPtySession> SpawnAsync(
        string shellPath, string[] args, string cwd, int cols, int rows,
        IDictionary<string, string> environment, CancellationToken ct)
    {
        var options = new PtyOptions
        {
            App = shellPath,
            CommandLine = args,
            Cwd = cwd,
            Cols = cols,
            Rows = rows,
            Environment = environment,
            ForceWinPty = true,  // ConPTY native shim isn't packaged with Quick.PtyNet; use WinPTY backend
        };
        var conn = await PtyProvider.SpawnAsync(options, ct);
        return new PtyNetSession(conn);
    }
}

internal sealed class PtyNetSession : IPtySession
{
    private readonly IPtyConnection conn;
    private int? exitCode;
    public event EventHandler<int?>? Exited;

    public PtyNetSession(IPtyConnection conn)
    {
        this.conn = conn;
        conn.ProcessExited += (_, e) =>
        {
            exitCode = e.ExitCode;
            Exited?.Invoke(this, e.ExitCode);
        };
    }

    public int Pid => conn.Pid;
    public int? ExitCode => exitCode;

    public Task WriteAsync(byte[] data, CancellationToken ct) =>
        conn.WriterStream.WriteAsync(data, 0, data.Length, ct);

    public Task<int> ReadAsync(byte[] buffer, CancellationToken ct) =>
        conn.ReaderStream.ReadAsync(buffer, 0, buffer.Length, ct);

    public void Resize(int cols, int rows) => conn.Resize(cols, rows);

    public void Dispose()
    {
        try { conn.Dispose(); } catch { /* best-effort */ }
    }
}
