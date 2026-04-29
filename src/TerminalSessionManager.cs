using System.Collections.Concurrent;

namespace MxStudioProTerminal;

public sealed class TerminalSessionManager : IDisposable
{
    private readonly IPtyFactory factory;
    private readonly ConcurrentDictionary<string, SessionState> sessions = new();
    private bool disposed;

    public event Action<string, byte[]>? Output;     // (tabId, bytes)  — populated in Task 9
    public event Action<string, int?>? Exited;       // (tabId, exitCode)

    public TerminalSessionManager(IPtyFactory factory)
    {
        this.factory = factory;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public async Task<string> CreateSessionAsync(string shellPath, string[] args, string cwd, int cols, int rows, CancellationToken ct = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(TerminalSessionManager));

        var env = BuildEnvironment();
        var pty = await factory.SpawnAsync(shellPath, args, cwd, cols, rows, env, ct);
        var tabId = Guid.NewGuid().ToString("N");
        var state = new SessionState(tabId, shellPath, cwd, pty);
        pty.Exited += (_, code) => OnPtyExited(tabId, code);
        sessions[tabId] = state;
        return tabId;
    }

    public IReadOnlyList<SessionInfo> ListSessions() =>
        sessions.Values.Select(s => new SessionInfo(
            s.TabId, TitleFor(s.ShellPath), s.ShellPath, s.Cwd, s.Pty.ExitCode is null
        )).ToList();

    public void Write(string tabId, byte[] data)
    {
        if (!sessions.TryGetValue(tabId, out var s)) return;
        try { s.Pty.WriteAsync(data, CancellationToken.None).GetAwaiter().GetResult(); }
        catch { /* PTY may have died — Exited handler removes it */ }
    }

    public void Resize(string tabId, int cols, int rows)
    {
        if (sessions.TryGetValue(tabId, out var s))
            try { s.Pty.Resize(cols, rows); } catch { /* best-effort */ }
    }

    public void Close(string tabId)
    {
        if (sessions.TryRemove(tabId, out var s))
            s.Pty.Dispose();
    }

    public void DisposeAll()
    {
        foreach (var key in sessions.Keys.ToList())
            Close(key);
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
        if (sessions.TryRemove(tabId, out _))
            Exited?.Invoke(tabId, code);
    }

    private static string TitleFor(string shellPath) =>
        Path.GetFileNameWithoutExtension(shellPath);

    private static IDictionary<string,string> BuildEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            env[(string)e.Key] = (string)(e.Value ?? "");

        // Strip Claude-Code session vars to avoid "nested session" errors
        env.Remove("CLAUDECODE");
        env.Remove("CLAUDE_CODE_ENTRY_POINT");
        env.Remove("CLAUDE_CODE_PARENT_SESSION_ID");

        // Set terminal hints
        env["COLORTERM"] = "truecolor";
        env["TERM"] = "xterm-256color";
        env["MCP_TIMEOUT"] = "15000";
        return env;
    }

    private sealed record SessionState(string TabId, string ShellPath, string Cwd, IPtySession Pty);
}
