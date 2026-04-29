using System.Text;
using System.Threading.Channels;
using Pty.Net;

namespace MxStudioProTerminal;

/// <summary>
/// Real Pty.Net-backed implementation of <see cref="IPtyFactory"/>.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// Pty.Net 0.1.16-pre API — deviations from original plan assumptions
/// ─────────────────────────────────────────────────────────────────────────────
/// PLAN ASSUMED                       REALITY (0.1.16-pre, dgriffen/Pty.Net)
/// ──────────────────────────────────────────────────────────────────────────
/// PtyProvider.SpawnAsync(PtyOptions)  PtyProvider.Spawn(string command,
///                                       int width, int height,
///                                       string workingDirectory,
///                                       BackendOptions = Default)
///   – synchronous, not async
///   – no PtyOptions class; no App/CommandLine/Cwd/Environment properties
///   – args must be appended to the command string
///   – environment injection not supported; child inherits host environment
///
/// IPtyConnection.ReaderStream (Stream) NOT EXPOSED on the public interface.
///   Exists on the internal PtyConnectionBase as OutputStream (Stream) but
///   is consumed by an internal ProcessPtyOutput background loop that fires
///   PtyData events. Direct reads race with that thread.
///
/// IPtyConnection.WriterStream (Stream) NOT EXPOSED. Public API is
///   Write(string) / Write(char) / WriteAsync(string) / WriteAsync(char).
///
/// IPtyConnection.Pid                  NOT EXPOSED on any public member.
///
/// IPtyConnection.ProcessExited event  NOT present. Disconnection fires
///   PtyDisconnected (no exit code).
///
/// IPtyConnection.ExitCode             NOT EXPOSED. Pty.Net 0.1.16-pre does
///   not surface the subprocess exit code.
///
/// Known limitation: on Windows, Pty.Net 0.1.16-pre does not route the
/// subprocess's stdout/stderr through the ConPTY/WinPTY output pipe into the
/// PtyData event. Only the VT-protocol initialization handshake bytes
/// ("\e[?9001h\e[?1004h") arrive via PtyData/OutputStream. Subprocess console
/// output goes directly to the Win32 console session of the host process.
/// This is a known deficiency in this early pre-release.
/// Task 8 and beyond should plan to migrate to a newer Pty.Net release or an
/// alternative (e.g., Pty.Net from microsoft/vs-pty.net) that exposes
/// ReaderStream / WriterStream with correct pipe plumbing.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class PtyNetFactory : IPtyFactory
{
    public Task<IPtySession> SpawnAsync(
        string shellPath,
        string[] args,
        string cwd,
        int cols,
        int rows,
        IDictionary<string, string> environment,
        CancellationToken ct)
    {
        // Build the command string: "shellPath arg1 arg2 ..."
        // Arguments with spaces should be pre-quoted by the caller.
        var command = args.Length == 0
            ? shellPath
            : shellPath + " " + string.Join(" ", args);

        // PtyProvider.Spawn is synchronous but may block briefly on native init;
        // run on the thread pool and propagate cancellation.
        return Task.Run<IPtySession>(() =>
        {
            ct.ThrowIfCancellationRequested();
            // Throws if the executable is not found or spawn fails at the Win32 level.
            var conn = PtyProvider.Spawn(command, cols, rows, cwd);
            return new PtyNetSession(conn);
        }, ct);
    }
}

/// <summary>
/// Adapts the event-driven <see cref="IPtyConnection"/> to the stream-like
/// <see cref="IPtySession"/> interface.
///
/// Output from the PTY arrives via the <c>PtyData</c> event (as strings) and is
/// queued through a <see cref="Channel{T}"/> so <see cref="ReadAsync"/> can be
/// awaited without busy-polling.
///
/// NOTE: Due to a known limitation in Pty.Net 0.1.16-pre, only the VT-protocol
/// initialization bytes arrive via PtyData. Subprocess stdout does not flow
/// through the managed pipe in this version.
/// </summary>
internal sealed class PtyNetSession : IPtySession
{
    private readonly IPtyConnection _conn;
    private readonly Channel<byte[]> _outputChannel;
    private byte[] _overflow = Array.Empty<byte>();
    private int? _exitCode;

    public event EventHandler<int?>? Exited;

    public PtyNetSession(IPtyConnection conn)
    {
        _conn = conn;

        // Unbounded: PTY output is bursty; never block the data event callback.
        _outputChannel = Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        conn.PtyData += OnPtyData;
        conn.PtyDisconnected += OnPtyDisconnected;
    }

    // Pty.Net 0.1.16-pre does not expose the process PID on the public interface.
    public int Pid => -1;

    public int? ExitCode => _exitCode;

    // -----------------------------------------------------------------------
    // Event handlers (called on Pty.Net's internal background thread)
    // -----------------------------------------------------------------------

    private void OnPtyData(object? sender, string data)
    {
        if (string.IsNullOrEmpty(data)) return;
        var bytes = Encoding.UTF8.GetBytes(data);
        // TryWrite never blocks on an unbounded channel.
        _outputChannel.Writer.TryWrite(bytes);
    }

    private void OnPtyDisconnected(object? sender)
    {
        // Pty.Net 0.1.16-pre does not expose the subprocess exit code.
        _exitCode = null;
        _outputChannel.Writer.TryComplete();
        Exited?.Invoke(this, null);
    }

    // -----------------------------------------------------------------------
    // IPtySession
    // -----------------------------------------------------------------------

    public async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        var text = Encoding.UTF8.GetString(data);
        await _conn.WriteAsync(text).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the next available bytes from the PTY output channel.
    /// Blocks until data arrives or the process disconnects.
    /// Returns 0 when the channel has been completed (process exited) and
    /// all queued data has been drained.
    /// </summary>
    public async Task<int> ReadAsync(byte[] buffer, CancellationToken ct)
    {
        // Drain any leftover bytes from a previous oversized chunk.
        if (_overflow.Length > 0)
            return DrainOverflow(buffer);

        while (await _outputChannel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            if (_outputChannel.Reader.TryRead(out var chunk) && chunk.Length > 0)
            {
                if (chunk.Length <= buffer.Length)
                {
                    chunk.CopyTo(buffer, 0);
                    return chunk.Length;
                }

                // Chunk larger than caller's buffer: copy what fits, stash the rest.
                Buffer.BlockCopy(chunk, 0, buffer, 0, buffer.Length);
                _overflow = chunk[buffer.Length..];
                return buffer.Length;
            }
        }

        // Channel completed — process exited, no more data.
        return 0;
    }

    public void Resize(int cols, int rows) => _conn.Resize(cols, rows);

    public void Dispose()
    {
        _conn.PtyData -= OnPtyData;
        _conn.PtyDisconnected -= OnPtyDisconnected;
        _outputChannel.Writer.TryComplete();
        try { _conn.Dispose(); } catch { /* best-effort */ }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private int DrainOverflow(byte[] buffer)
    {
        var take = Math.Min(_overflow.Length, buffer.Length);
        Buffer.BlockCopy(_overflow, 0, buffer, 0, take);
        _overflow = take < _overflow.Length ? _overflow[take..] : Array.Empty<byte>();
        return take;
    }
}
