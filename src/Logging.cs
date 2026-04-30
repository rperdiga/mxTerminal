namespace Terminal;

public sealed class Logger
{
    private readonly string projectDir;
    private readonly object gate = new();

    public Logger(string projectDir) => this.projectDir = projectDir;

    public void Info(string message)  => Write("INFO",  message, exception: null);
    public void Warn(string message)  => Write("WARN",  message, exception: null);
    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    public void Clear()
    {
        try
        {
            var path = LogPath();
            if (path != null && File.Exists(path)) File.WriteAllText(path, string.Empty);
        }
        catch { /* best-effort */ }
    }

    private string? LogPath()
    {
        try
        {
            var dir = Path.Combine(projectDir, "resources");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "terminal.log");
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
