using System;
using System.IO;

namespace Concord.Shim;

/// <summary>
/// Pre-HostContext logger. Writes to %TEMP%\Concord\shim.log with rolling
/// truncation at 1 MB. Safe to call before any Concord.Core type has been
/// touched. After HostContext.Initialize the host's Logger takes over;
/// ShimLog is only used during load-context bootstrap and for errors that
/// happen before the host has been instantiated.
/// </summary>
internal static class ShimLog
{
    private static readonly object _gate = new();
    private static readonly string _path =
        Path.Combine(Path.GetTempPath(), "Concord", "shim.log");
    private const long MaxBytes = 1_000_000;

    static ShimLog()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); }
        catch { /* logger must never throw */ }
    }

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null)
    {
        Write("ERROR", message + (ex is null ? "" : $"\n{ex}"));
    }

    /// <summary>
    /// Times the given action; logs "<label> took Xms" at INFO. Returns the
    /// action's result. Used by HostKickstart to feed Phase 5's perf matrix.
    /// </summary>
    public static T Timed<T>(string label, Func<T> action)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { return action(); }
        finally
        {
            sw.Stop();
            Info($"{label} took {sw.ElapsedMilliseconds}ms");
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                RollIfTooLarge();
                using var sw = new StreamWriter(_path, append: true);
                sw.WriteLine($"[{DateTime.UtcNow:O}] {level} {message}");
            }
        }
        catch { /* logger must never throw */ }
    }

    private static void RollIfTooLarge()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var len = new FileInfo(_path).Length;
            if (len < MaxBytes) return;
            var backup = _path + ".1";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(_path, backup);
        }
        catch { /* logger must never throw */ }
    }
}
