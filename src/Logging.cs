namespace Terminal;

public sealed class Logger
{
    private readonly string projectDir;
    private readonly object gate = new();

    public Logger(string projectDir) => this.projectDir = projectDir;

    /// <summary>
    /// v4.2.0+: when true, <see cref="Debug"/> calls write to the log;
    /// otherwise they no-op. Toggled by the settings save-path when the user
    /// flips "Diagnostic logging" in Settings → Concord MCP. Default false:
    /// keeps the log free of CDP traffic for users who don't need it.
    /// </summary>
    public bool DiagnosticEnabled { get; set; }

    public void Info(string message)  => Write("INFO",  message, exception: null);
    public void Warn(string message)  => Write("WARN",  message, exception: null);
    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    /// <summary>
    /// v4.2.0+: diagnostic-only line. No-op when <see cref="DiagnosticEnabled"/>
    /// is false. Used by Maia / CDP code to trace request/response shapes
    /// without polluting the terminal.log of users who don't have the toggle on.
    /// </summary>
    public void Debug(string message)
    {
        if (!DiagnosticEnabled) return;
        Write("DEBUG", message, exception: null);
    }

    public void Clear()
    {
        try
        {
            var path = LogPath();
            if (path != null && File.Exists(path)) File.WriteAllText(path, string.Empty);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Public so the About panel can show the current log path.</summary>
    public string? Path => LogPath();

    private string? LogPath()
    {
        try
        {
            var dir = System.IO.Path.Combine(projectDir, "resources");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "terminal.log");
        }
        catch
        {
            return null;
        }
    }

    private void Write(string level, string message, Exception? exception)
    {
        var path = LogPath();
        if (path is null) return;

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {level} {message}";
        if (exception != null)
            line += $"{Environment.NewLine}    {exception.GetType().Name}: {exception.Message}{Environment.NewLine}    {exception.StackTrace}";
        line += Environment.NewLine;

        try
        {
            lock (gate) File.AppendAllText(path, line);
        }
        catch { /* best-effort */ }
    }
}
