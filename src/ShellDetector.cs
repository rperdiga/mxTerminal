namespace MxStudioProTerminal;

public sealed record ShellOption(string Name, string Path);

/// <summary>
/// Detects shells available on the host machine. Returns a curated list with
/// well-known names; entries that aren't present are simply omitted (except
/// for the always-present cmd.exe / powershell.exe which Windows ships).
/// </summary>
public static class ShellDetector
{
    public static IReadOnlyList<ShellOption> Detect()
    {
        var results = new List<ShellOption>
        {
            // Always present on Windows.
            new("Windows PowerShell", "powershell.exe"),
            new("Command Prompt", "cmd.exe"),
        };

        // Optional: PowerShell 7+ (pwsh.exe). Check PATH.
        var pwsh = ResolveOnPath("pwsh.exe");
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

    private static string? ResolveOnPath(string exe)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var full = Path.Combine(dir, exe);
                if (File.Exists(full)) return full;
            }
            catch { /* invalid path entry — skip */ }
        }
        return null;
    }
}
