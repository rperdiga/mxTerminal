using System.Collections.Concurrent;

namespace Terminal;

public sealed class TerminalSessionManager : IDisposable
{
    private const int CoalesceMillis = 16;

    private readonly IPtyFactory factory;
    private readonly int ringBufferBytes;
    private readonly ConcurrentDictionary<string, SessionState> sessions = new();
    private readonly object ordinalGate = new();
    private bool disposed;
    private StudioProActionServer? actionServer;
    private readonly object actionServerGate = new();

    public event Action<string, byte[]>? Output;
    public event Action<string, int?>? Exited;
    /// <summary>Fires after sessions dict mutates (create/close/exit).
    /// Listeners typically persist the new tab state to disk.</summary>
    public event Action? SessionsChanged;

    /// <summary>Tabs that exited cleanly (code 0). Cross-session restore skips these.</summary>
    private readonly HashSet<int> exitedCleanOrdinals = new();

    public TerminalSessionManager(IPtyFactory factory, int ringBufferBytes = 4 * 1024 * 1024)
    {
        this.factory = factory;
        this.ringBufferBytes = ringBufferBytes;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public async Task<(string TabId, string Title)> CreateSessionAsync(
        string shellPath, string[] args, string cwd, int cols, int rows, CancellationToken ct = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(TerminalSessionManager));

        var env = BuildEnvironment();
        var pty = await factory.SpawnAsync(shellPath, args, cwd, cols, rows, env, ct);
        var tabId = Guid.NewGuid().ToString("N");
        // Pick the smallest unused positive integer among currently-active tabs.
        // This keeps the visible numbers tight (close #2, next new tab fills #2)
        // and means the first tab is always #1 — Neo's "the number should be
        // based on the current count, not a lifetime counter".
        var ordinal = NextOrdinal();
        var title = TitleFor(shellPath, ordinal);
        var state = new SessionState(tabId, shellPath, args, cwd, cols, rows, pty, new RingBuffer(ringBufferBytes), title, ordinal);
        state.AttachTimer(_ => FlushPending(state));
        pty.Exited += (_, code) => OnPtyExited(tabId, code);

        sessions[tabId] = state;
        _ = Task.Run(() => ReadLoopAsync(state, state.Cts.Token));
        SessionsChanged?.Invoke();
        return (tabId, title);
    }

    /// <summary>
    /// Snapshot the current tabs in a form suitable for persistence to
    /// terminal-state.json. Includes only tabs whose PTY is still alive — we
    /// don't carry "exited cleanly" tabs across restart.
    /// </summary>
    public TerminalState SnapshotState()
    {
        var tabs = sessions.Values
            .Where(s => s.Pty.ExitCode is null && !exitedCleanOrdinals.Contains(s.Ordinal))
            .OrderBy(s => s.Ordinal)
            .Select(s => new TerminalState.Tab(s.Title, s.ShellPath, s.Args, s.Ordinal))
            .ToList();
        return new TerminalState(tabs, ActiveTabOrdinal: tabs.FirstOrDefault()?.Ordinal);
    }

    public int SessionCount => sessions.Count;

    public IReadOnlyList<SessionInfo> ListSessions() =>
        sessions.Values.Select(s => new SessionInfo(
            s.TabId, s.Title, s.ShellPath, s.Cwd, s.Pty.ExitCode is null
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
            SessionsChanged?.Invoke();
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
            var (newId, newTitle) = await CreateSessionAsync(snap.ShellPath, snap.Args, snap.Cwd, snap.Cols, snap.Rows, ct);
            recycled.Add(new RecycledSession(snap.TabId, newId, newTitle, snap.ShellPath, snap.Cwd));
        }
        return recycled;
    }

    public int? CurrentActionServerPort
    {
        get { lock (actionServerGate) return actionServer?.Port; }
    }

    public void StartActionServer(int port, StudioProActions actions, Logger? log = null)
    {
        if (disposed) throw new ObjectDisposedException(nameof(TerminalSessionManager));
        lock (actionServerGate)
        {
            // Always rebuild — the caller may have constructed a fresh `actions` with
            // updated hotkey config that we can't detect from here. The cost is one
            // TCP listener bind cycle, which is cheap.
            actionServer?.Dispose();
            var s = new StudioProActionServer(actions, port, log);
            s.Start();
            actionServer = s;
        }
    }

    public void StopActionServer()
    {
        lock (actionServerGate)
        {
            actionServer?.Dispose();
            actionServer = null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        StopActionServer();
        DisposeAll();
    }

    private void OnProcessExit(object? sender, EventArgs e) => DisposeAll();

    private void OnPtyExited(string tabId, int? code)
    {
        if (sessions.TryRemove(tabId, out var s))
        {
            s.Cts.Cancel();
            // Track exit-clean tabs so we don't try to restore them across
            // Studio Pro restart. Exit code 0 means user explicitly exited
            // (e.g. typed `exit`) — bringing it back would be surprising.
            if (code == 0) exitedCleanOrdinals.Add(s.Ordinal);
            Exited?.Invoke(tabId, code);
            SessionsChanged?.Invoke();
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

    /// <summary>
    /// Tab title format: "&lt;Shell&gt; - &lt;ordinal&gt;" (Title-cased shell + hyphen + count).
    /// Examples: "Pwsh - 1", "Bash - 2", "Cmd - 3". Ordinal is the smallest unused
    /// positive integer among currently-open tabs (gap-filling).
    /// </summary>
    private static string TitleFor(string shellPath, int ordinal) =>
        $"{ShellLabel(shellPath)} - {ordinal}";

    /// <summary>
    /// Map a shell exe path to a friendly Title-cased label.
    /// powershell.exe / pwsh.exe → "Pwsh", bash.exe → "Bash", cmd.exe → "Cmd",
    /// zsh / fish stay verbatim (Title-cased).
    /// Anything else falls back to Title-cased file-name-without-extension.
    /// </summary>
    internal static string ShellLabel(string shellPath)
    {
        var name = Path.GetFileNameWithoutExtension(shellPath).ToLowerInvariant();
        var canonical = name switch
        {
            "powershell" or "pwsh" => "pwsh",
            "cmd"                  => "cmd",
            "bash"                 => "bash",
            "wsl"                  => "wsl",
            _                      => name,
        };
        return canonical.Length > 0
            ? char.ToUpperInvariant(canonical[0]) + canonical.Substring(1)
            : canonical;
    }

    /// <summary>
    /// Returns the smallest positive integer not currently used by any active
    /// session's Ordinal. Thread-safe via ordinalGate so two concurrent
    /// CreateSessionAsync calls don't race to the same number.
    /// </summary>
    private int NextOrdinal()
    {
        lock (ordinalGate)
        {
            var used = new HashSet<int>(sessions.Values.Select(s => s.Ordinal));
            for (int i = 1; ; i++)
                if (!used.Contains(i)) return i;
        }
    }

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
        public string Title { get; }
        public int Ordinal { get; }
        public List<byte[]> Pending { get; } = new();
        public bool TimerArmed { get; set; }
        public Timer Timer { get; private set; } = null!;
        public CancellationTokenSource Cts { get; } = new();
        public object Gate { get; } = new();

        public SessionState(string tabId, string shellPath, string[] args, string cwd, int cols, int rows, IPtySession pty, RingBuffer ring, string title, int ordinal)
        {
            TabId = tabId; ShellPath = shellPath; Args = args; Cwd = cwd;
            Cols = cols; Rows = rows; Pty = pty; Ring = ring; Title = title; Ordinal = ordinal;
        }

        public void AttachTimer(TimerCallback cb) => Timer = new Timer(cb);
    }
}
