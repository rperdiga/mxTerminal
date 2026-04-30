using Pty.Net;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Terminal;

public sealed class PtyNetFactory : IPtyFactory
{
    static PtyNetFactory()
    {
        // Studio Pro loads our DLL via MEF, so .NET's automatic resolution of
        // runtimes/<rid>/native/ entries from deps.json doesn't fire — Pty.Net's
        // [DllImport("winpty.dll")] hits a DllNotFoundException because the
        // default Win32 search path doesn't include our extension folder.
        // Install a manual resolver that loads winpty.dll from next to this
        // assembly (we copy it there in Terminal.csproj).
        NativeLibrary.SetDllImportResolver(typeof(PtyProvider).Assembly, (libName, asm, searchPath) =>
        {
            if (!libName.Equals("winpty.dll", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            var here = Path.GetDirectoryName(typeof(PtyNetFactory).Assembly.Location);
            if (string.IsNullOrEmpty(here)) return IntPtr.Zero;

            // Try the flattened copy first, then the runtimes/<rid>/native/ layout.
            string[] candidates =
            {
                Path.Combine(here, "winpty.dll"),
                Path.Combine(here, "runtimes", "win-x64", "native", "winpty.dll"),
            };
            foreach (var path in candidates)
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                    return handle;

            return IntPtr.Zero;
        });
    }

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
