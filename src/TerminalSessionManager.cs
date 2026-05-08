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
        args = InjectShellInitArgs(shellPath, args);
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

    public Task Write(string tabId, byte[] data)
    {
        if (!sessions.TryGetValue(tabId, out var s)) return Task.CompletedTask;
        // Always offload to the thread pool. On macOS, Mendix's WKScriptMessage
        // handler delivers JS→C# messages on the main UI thread, so blocking on
        // WriteLock followed by sync PTY write would freeze Studio Pro (rainbow
        // beachball) on every keystroke. WebView2 on Windows happens to dispatch
        // off-thread, which masked this on the original code. Returning a Task
        // lets tests await for-deterministic completion; the production caller
        // discards it (fire-and-forget). Per-tab order is preserved by the
        // WriteLock semaphore.
        return Task.Run(async () =>
        {
            await s.WriteLock.WaitAsync();
            try { await s.Pty.WriteAsync(data, CancellationToken.None); }
            catch { /* PTY may have died — Exited handler removes it */ }
            finally { s.WriteLock.Release(); }
        });
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

    public void StartActionServer(
        int port,
        StudioProActions actions,
        Logger? log = null,
        Terminal.Maia.MaiaActions? maia = null,
        bool studioProActionsEnabled = true,
        bool maiaIntegrationEnabled = false)
    {
        if (disposed) throw new ObjectDisposedException(nameof(TerminalSessionManager));
        lock (actionServerGate)
        {
            // Always rebuild — the caller may have constructed a fresh `actions` with
            // updated hotkey config that we can't detect from here. The cost is one
            // TCP listener bind cycle, which is cheap.
            actionServer?.Dispose();
            var s = new StudioProActionServer(actions, port, log, maia, studioProActionsEnabled, maiaIntegrationEnabled);
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

        // Override the zsh prompt to a short form (current dir + %), avoiding
        // macOS's verbose default `user@host dirname %`. We do this via
        // ZDOTDIR: zsh reads its rc files from $ZDOTDIR if set, so we point
        // at a Concord-owned dir whose .zshrc sources the user's real
        // config and then overrides PROMPT. Non-zsh shells ignore ZDOTDIR.
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            try
            {
                var zdotdir = EnsureZshConfigDir();
                if (zdotdir != null) env["ZDOTDIR"] = zdotdir;
            }
            catch { /* best-effort — non-fatal if we can't write the dotfiles */ }

            // POSIX sh / dash / bash-as-sh read $ENV for interactive shells —
            // there is no rcfile flag for them. Point at a sh-compatible
            // init file so /bin/sh tabs also pick up PATH and a short prompt.
            try
            {
                var envFile = EnsurePosixShInitFile();
                if (envFile != null) env["ENV"] = envFile;
            }
            catch { /* best-effort */ }
        }
        return env;
    }

    /// <summary>
    /// Inject shell-specific init flags so the spawned shell loads the user's
    /// real environment (PATH, aliases, etc.) and gets the Concord short
    /// prompt. zsh is handled via <c>ZDOTDIR</c> in <see cref="BuildEnvironment"/>;
    /// bash needs <c>--rcfile</c> on the command line because it has no
    /// equivalent env var for interactive shells. Other shells pass through
    /// unchanged.
    /// </summary>
    private static string[] InjectShellInitArgs(string shellPath, string[] args)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) return args;
        var name = Path.GetFileName(shellPath);
        if (!string.Equals(name, "bash", StringComparison.OrdinalIgnoreCase)) return args;

        // If the user (or a previous tab restore) already passed --rcfile or
        // --init-file, respect that.
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--rcfile" || args[i] == "--init-file") return args;
        }

        try
        {
            var rc = EnsureBashInitFile();
            if (rc == null) return args;
            // bash requires the rcfile flag to come before any other args; we
            // also need --rcfile to actually take effect, the shell must be
            // interactive (which a PTY-backed shell is by default), so no -i.
            return new[] { "--rcfile", rc }.Concat(args).ToArray();
        }
        catch { return args; }
    }

    /// <summary>
    /// Materialize a Concord-owned <c>$ENV</c> init file for POSIX shells
    /// (/bin/sh, dash, bash-as-sh). Adds the same PATH augmentations as the
    /// bash rcfile — but in pure POSIX syntax, with no bash-specific PS1
    /// escapes. Sets a plain <c>$ </c> prompt because POSIX sh has no
    /// per-prompt expansion equivalent to bash <c>\W</c>; the shorter form
    /// is still less noisy than the system default.
    /// </summary>
    private static string? EnsurePosixShInitFile()
    {
        string baseDir;
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, "Library", "Application Support", "Concord", "sh");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, ".config", "concord", "sh");
        }
        Directory.CreateDirectory(baseDir);

        var rc = Path.Combine(baseDir, "concord_shrc");
        File.WriteAllText(rc,
            "# Concord-managed POSIX-sh init (read via $ENV).\n" +
            "# Source the user's bash login files in case they keep PATH there.\n" +
            "[ -f /etc/profile ]          && . /etc/profile\n" +
            "[ -f \"$HOME/.profile\" ]      && . \"$HOME/.profile\"\n" +
            "[ -f \"$HOME/.bash_profile\" ] && . \"$HOME/.bash_profile\"\n" +
            "[ -f \"$HOME/.bashrc\" ]       && . \"$HOME/.bashrc\"\n" +
            "\n" +
            "# Same PATH augmentation as concord_bashrc — covers the case where\n" +
            "# the user keeps no sh/bash files and configures PATH only in\n" +
            "# zsh-specific files.\n" +
            "_concord_path_prepend() {\n" +
            "  case \":$PATH:\" in *\":$1:\"*) ;; *) [ -d \"$1\" ] && PATH=\"$1:$PATH\" ;; esac\n" +
            "}\n" +
            "_concord_path_prepend \"/opt/homebrew/bin\"\n" +
            "_concord_path_prepend \"/opt/homebrew/sbin\"\n" +
            "_concord_path_prepend \"/usr/local/bin\"\n" +
            "_concord_path_prepend \"$HOME/.npm-global/bin\"\n" +
            "_concord_path_prepend \"$HOME/.local/bin\"\n" +
            "unset -f _concord_path_prepend\n" +
            "export PATH\n" +
            "\n" +
            "PS1='$ '\n");
        return rc;
    }

    /// <summary>
    /// Materialize a Concord-owned bash init file that sources the user's
    /// real bash config (in the order an interactive login bash would) and
    /// then trims <c>PS1</c> to <c>\W \$ </c>. Returns the path, or null on
    /// failure. Idempotent.
    /// </summary>
    private static string? EnsureBashInitFile()
    {
        string baseDir;
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, "Library", "Application Support", "Concord", "bash");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, ".config", "concord", "bash");
        }
        Directory.CreateDirectory(baseDir);

        var rc = Path.Combine(baseDir, "concord_bashrc");
        File.WriteAllText(rc,
            "# Concord-managed bash init.\n" +
            "# Source the user's real init files in the order an interactive login\n" +
            "# bash would, so PATH, aliases, and brew/node setup all apply.\n" +
            "[ -f /etc/profile ] && . /etc/profile\n" +
            "[ -f \"$HOME/.bash_profile\" ] && . \"$HOME/.bash_profile\"\n" +
            "[ -f \"$HOME/.bashrc\" ] && . \"$HOME/.bashrc\"\n" +
            "\n" +
            "# Many users keep no bash files and rely on zsh-only PATH exports —\n" +
            "# in that case the sources above are a no-op and bash sees only the\n" +
            "# launchd-default PATH (no ~/.local/bin, no homebrew, no npm globals).\n" +
            "# Prepend the common locations so user-installed CLIs (claude, codex,\n" +
            "# copilot, brew-installed tools) resolve. Each entry is added only\n" +
            "# if the dir exists AND isn't already on PATH.\n" +
            "_concord_path_prepend() {\n" +
            "  case \":$PATH:\" in *\":$1:\"*) ;; *) [ -d \"$1\" ] && PATH=\"$1:$PATH\" ;; esac\n" +
            "}\n" +
            "_concord_path_prepend \"/opt/homebrew/bin\"\n" +
            "_concord_path_prepend \"/opt/homebrew/sbin\"\n" +
            "_concord_path_prepend \"/usr/local/bin\"\n" +
            "_concord_path_prepend \"$HOME/.npm-global/bin\"\n" +
            "_concord_path_prepend \"$HOME/.local/bin\"\n" +
            "unset -f _concord_path_prepend\n" +
            "export PATH\n" +
            "\n" +
            "PS1='\\W \\$ '\n");
        return rc;
    }

    /// <summary>
    /// Materialize a Concord-owned ZDOTDIR with minimal zsh init files that
    /// source the user's real config and then trim the prompt to
    /// <c>%1~ %# </c>. Returns the directory path, or null if creation fails.
    /// Idempotent — overwrites the rc files each time so prompt edits ship
    /// with new builds without stale caches.
    /// </summary>
    private static string? EnsureZshConfigDir()
    {
        // ~/Library/Application Support/Concord/zsh on Mac;
        // ~/.config/concord/zsh on Linux (XDG-ish — close enough).
        string baseDir;
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, "Library", "Application Support", "Concord", "zsh");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, ".config", "concord", "zsh");
        }
        Directory.CreateDirectory(baseDir);

        // .zshenv runs for ALL invocations (login, interactive, scripts).
        // Source the user's real .zshenv if it exists so PATH etc. still works.
        // We must NOT set ZDOTDIR here, or we'd loop.
        File.WriteAllText(Path.Combine(baseDir, ".zshenv"),
            "# Concord-managed — sources the user's real .zshenv\n" +
            "[ -f \"$HOME/.zshenv\" ] && . \"$HOME/.zshenv\"\n");

        // .zprofile runs for login shells (before .zshrc). Same forwarding.
        File.WriteAllText(Path.Combine(baseDir, ".zprofile"),
            "# Concord-managed — sources the user's real .zprofile\n" +
            "[ -f \"$HOME/.zprofile\" ] && . \"$HOME/.zprofile\"\n");

        // .zshrc runs for interactive shells. Source the user's then trim
        // PROMPT. Use %1~ for the deepest path component (~ for home), and
        // %# for $/% based on privilege.
        File.WriteAllText(Path.Combine(baseDir, ".zshrc"),
            "# Concord-managed — sources the user's real .zshrc, then trims PROMPT.\n" +
            "[ -f \"$HOME/.zshrc\" ] && . \"$HOME/.zshrc\"\n" +
            "PROMPT='%1~ %# '\n" +
            "RPROMPT=''\n");

        // .zlogin runs after .zshrc for login shells. Forward to user's.
        File.WriteAllText(Path.Combine(baseDir, ".zlogin"),
            "# Concord-managed — sources the user's real .zlogin\n" +
            "[ -f \"$HOME/.zlogin\" ] && . \"$HOME/.zlogin\"\n");

        return baseDir;
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
        // Serializes Write() calls for this tab. Chunked paste delivers multiple
        // input messages in flight; without this they can interleave on the PTY
        // writer because GetAwaiter().GetResult() only blocks the calling thread,
        // not parallel callers from different bridge dispatch threads.
        public SemaphoreSlim WriteLock { get; } = new(1, 1);

        public SessionState(string tabId, string shellPath, string[] args, string cwd, int cols, int rows, IPtySession pty, RingBuffer ring, string title, int ordinal)
        {
            TabId = tabId; ShellPath = shellPath; Args = args; Cwd = cwd;
            Cols = cols; Rows = rows; Pty = pty; Ring = ring; Title = title; Ordinal = ordinal;
        }

        public void AttachTimer(TimerCallback cb) => Timer = new Timer(cb);
    }
}
