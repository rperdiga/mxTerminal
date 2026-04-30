using System.Collections.Concurrent;

namespace MxStudioProTerminal;

public sealed class TerminalSessionManager : IDisposable
{
    private const int CoalesceMillis = 16;

    private readonly IPtyFactory factory;
    private readonly int ringBufferBytes;
    private readonly ConcurrentDictionary<string, SessionState> sessions = new();
    private bool disposed;

    public event Action<string, byte[]>? Output;
    public event Action<string, int?>? Exited;

    public TerminalSessionManager(IPtyFactory factory, int ringBufferBytes = 4 * 1024 * 1024)
    {
        this.factory = factory;
        this.ringBufferBytes = ringBufferBytes;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public async Task<string> CreateSessionAsync(
        string shellPath, string[] args, string cwd, int cols, int rows, CancellationToken ct = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(TerminalSessionManager));

        var env = BuildEnvironment();
        var pty = await factory.SpawnAsync(shellPath, args, cwd, cols, rows, env, ct);
        var tabId = Guid.NewGuid().ToString("N");
        var state = new SessionState(tabId, shellPath, args, cwd, cols, rows, pty, new RingBuffer(ringBufferBytes));
        state.AttachTimer(_ => FlushPending(state));
        pty.Exited += (_, code) => OnPtyExited(tabId, code);

        sessions[tabId] = state;
        _ = Task.Run(() => ReadLoopAsync(state, state.Cts.Token));
        return tabId;
    }

    public IReadOnlyList<SessionInfo> ListSessions() =>
        sessions.Values.Select(s => new SessionInfo(
            s.TabId, TitleFor(s.ShellPath), s.ShellPath, s.Cwd, s.Pty.ExitCode is null
        )).ToList();

    public byte[] SnapshotBuffer(string tabId)
    {
        if (!sessions.TryGetValue(tabId, out var s)) return Array.Empty<byte>();
        lock (s.Gate) return s.Ring.Snapshot();
    }

    public void Write(string tabId, byte[] data)
    {
        if (!sessions.TryGetValue(tabId, out var s)) return;
        try { s.Pty.WriteAsync(data, CancellationToken.None).GetAwaiter().GetResult(); }
        catch { /* PTY may have died — Exited handler removes it */ }
    }

    public void Resize(string tabId, int cols, int rows)
    {
        if (sessions.TryGetValue(tabId, out var s))
        {
            s.Cols = cols; s.Rows = rows;
            try { s.Pty.Resize(cols, rows); } catch { }
        }
    }

    public void Close(string tabId)
    {
        if (sessions.TryRemove(tabId, out var s))
        {
            s.Cts.Cancel();
            s.Pty.Dispose();
        }
    }

    public void DisposeAll()
    {
        foreach (var key in sessions.Keys.ToList())
            Close(key);
    }

    public sealed record RecycledSession(string OldTabId, string NewTabId, string Title, string ShellPath, string Cwd);

    /// <summary>
    /// Closes every active session and respawns it with the same shell/args/cwd
    /// and the most recently observed cols/rows. Returns one row per session
    /// describing the old → new tab id mapping so callers can update UIs.
    /// </summary>
    public async Task<IReadOnlyList<RecycledSession>> RecycleAllAsync(CancellationToken ct = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(TerminalSessionManager));

        // Snapshot before we start mutating the dictionary.
        var snapshots = sessions.Values
            .Select(s => (s.TabId, s.ShellPath, Args: s.Args.ToArray(), s.Cwd, s.Cols, s.Rows))
            .ToList();

        var recycled = new List<RecycledSession>();
        foreach (var snap in snapshots)
        {
            Close(snap.TabId);
            var newId = await CreateSessionAsync(snap.ShellPath, snap.Args, snap.Cwd, snap.Cols, snap.Rows, ct);
            recycled.Add(new RecycledSession(snap.TabId, newId, TitleFor(snap.ShellPath), snap.ShellPath, snap.Cwd));
        }
        return recycled;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        DisposeAll();
    }

    private void OnProcessExit(object? sender, EventArgs e) => DisposeAll();

    private void OnPtyExited(string tabId, int? code)
    {
        if (sessions.TryRemove(tabId, out var s))
        {
            s.Cts.Cancel();
            Exited?.Invoke(tabId, code);
        }
    }

    private async Task ReadLoopAsync(SessionState s, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n;
                try { n = await s.Pty.ReadAsync(buffer, ct); }
                catch (OperationCanceledException) { break; }
                catch { break; }

                if (n <= 0) break;

                var chunk = new byte[n];
                Array.Copy(buffer, chunk, n);

                lock (s.Gate)
                {
                    s.Ring.Write(chunk);
                    s.Pending.Add(chunk);
                    EnsureCoalesceTimerArmed_NoLock(s);
                }
            }
        }
        finally
        {
            // Final flush
            FlushPending(s);
        }
    }

    private void EnsureCoalesceTimerArmed_NoLock(SessionState s)
    {
        if (s.TimerArmed) return;
        s.TimerArmed = true;
        s.Timer.Change(CoalesceMillis, Timeout.Infinite);
    }

    private void FlushPending(SessionState s)
    {
        byte[] toEmit;
        lock (s.Gate)
        {
            s.TimerArmed = false;
            if (s.Pending.Count == 0) return;
            var total = s.Pending.Sum(c => c.Length);
            toEmit = new byte[total];
            var offset = 0;
            foreach (var chunk in s.Pending)
            {
                Array.Copy(chunk, 0, toEmit, offset, chunk.Length);
                offset += chunk.Length;
            }
            s.Pending.Clear();
        }
        Output?.Invoke(s.TabId, toEmit);
    }

    private static string TitleFor(string shellPath) =>
        Path.GetFileNameWithoutExtension(shellPath);

    private static IDictionary<string, string> BuildEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            env[(string)e.Key] = (string)(e.Value ?? "");
        env.Remove("CLAUDECODE");
        env.Remove("CLAUDE_CODE_ENTRY_POINT");
        env.Remove("CLAUDE_CODE_PARENT_SESSION_ID");
        env["COLORTERM"] = "truecolor";
        env["TERM"] = "xterm-256color";
        env["MCP_TIMEOUT"] = "15000";
        return env;
    }

    private sealed class SessionState
    {
        public string TabId { get; }
        public string ShellPath { get; }
        public string[] Args { get; }
        public string Cwd { get; }
        public int Cols { get; set; }
        public int Rows { get; set; }
        public IPtySession Pty { get; }
        public RingBuffer Ring { get; }
        public List<byte[]> Pending { get; } = new();
        public bool TimerArmed { get; set; }
        public Timer Timer { get; private set; } = null!;
        public CancellationTokenSource Cts { get; } = new();
        public object Gate { get; } = new();

        public SessionState(string tabId, string shellPath, string[] args, string cwd, int cols, int rows, IPtySession pty, RingBuffer ring)
        {
            TabId = tabId; ShellPath = shellPath; Args = args; Cwd = cwd;
            Cols = cols; Rows = rows; Pty = pty; Ring = ring;
        }

        public void AttachTimer(TimerCallback cb) => Timer = new Timer(cb);
    }
}
