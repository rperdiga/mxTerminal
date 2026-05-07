namespace Terminal;

public sealed record ShellOption(string Name, string Path);

/// <summary>
/// Detects shells available on the host machine. Returns a curated list with
/// well-known names; entries that aren't present are omitted.
/// </summary>
public static class ShellDetector
{
    public static IReadOnlyList<ShellOption> Detect() =>
        OperatingSystem.IsWindows() ? DetectWindows() : DetectUnix();

    private static IReadOnlyList<ShellOption> DetectWindows()
    {
        var results = new List<ShellOption>
        {
            // Always present on Windows.
            new("Windows PowerShell", "powershell.exe"),
            new("Command Prompt", "cmd.exe"),
        };

        // Optional: PowerShell 7+ (pwsh.exe). Check PATH.
        var pwsh = ResolveOnPath("pwsh");
        if (pwsh != null)
            results.Insert(0, new ShellOption("PowerShell 7+", pwsh));

        // Optional: Git Bash. Common install locations.
        string[] gitBashCandidates =
        {
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
        };
        foreach (var candidate in gitBashCandidates)
        {
            if (File.Exists(candidate))
            {
                results.Add(new ShellOption("Git Bash", candidate));
                break;
            }
        }

        return results;
    }

    private static IReadOnlyList<ShellOption> DetectUnix()
    {
        var results = new List<ShellOption>();

        // The user's login shell — always preferred when available.
        var loginShell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(loginShell) && File.Exists(loginShell))
            results.Add(new ShellOption($"Default Shell ({System.IO.Path.GetFileName(loginShell)})", loginShell));

        if (File.Exists("/bin/zsh"))
            results.Add(new ShellOption("Zsh", "/bin/zsh"));
        if (File.Exists("/bin/bash"))
            results.Add(new ShellOption("Bash", "/bin/bash"));
        // /bin/sh is mandatory on POSIX systems but include the existence
        // check anyway in case we're inside an unusual chroot.
        if (File.Exists("/bin/sh"))
            results.Add(new ShellOption("POSIX Shell", "/bin/sh"));

        // Optional: PowerShell 7+ on macOS/Linux (no .exe extension).
        var pwsh = ResolveOnPath("pwsh");
        if (pwsh != null)
            results.Add(new ShellOption("PowerShell 7+", pwsh));

        return results;
    }

    private static string? ResolveOnPath(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;
        // Try the bare name first (Mac/Linux) then with .exe (Windows). Either
        // works in either branch — the .exe is an extra fallback that never
        // matches on POSIX.
        string[] candidates = OperatingSystem.IsWindows()
            ? new[] { name + ".exe", name }
            : new[] { name, name + ".exe" };
        foreach (var dir in pathEnv.Split(System.IO.Path.PathSeparator))
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    var full = System.IO.Path.Combine(dir, candidate);
                    if (File.Exists(full)) return full;
                }
                catch { /* invalid path entry — skip */ }
            }
        }
        return null;
    }
}
