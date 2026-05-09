using System.Text.Json;

namespace Terminal;

public sealed record TerminalSettings(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines,
    string Theme,
    bool McpEnabled,
    int McpPort,
    string[] McpClients,
    bool McpServerEnabled,
    int McpServerPort,
    bool StudioProActionsEnabled,
    bool MaiaIntegrationEnabled,
    string RefreshFromDiskHotkey,
    bool RestoreTabsOnReopen,
    bool SkillsEnabled,
    string[] SkillClients)
{
    public static TerminalSettings Defaults() => new(
        ShellPath: DefaultShellPath(),
        Args: Array.Empty<string>(),
        RingBufferKB: 4096,
        XtermScrollbackLines: 10000,
        Theme: "auto",
        // v4.1.0: all-on except Codex. Codex writes to user-global
        // ~/.codex/config.toml — keeping it opt-in avoids touching state
        // outside the project tree without explicit consent.
        McpEnabled: true,
        // Studio Pro's standard MCP server port (HKLM\SOFTWARE\Mendix...).
        // The runtime always re-probes Studio Pro's actual port from
        // %LOCALAPPDATA%\Mendix\Settings.sqlite at save time — this is just
        // the fallback when the probe fails (e.g. locked DB).
        McpPort: 8100,
        McpClients: new[] { "claude", "copilot" },
        McpServerEnabled: true,
        McpServerPort: 7783,
        StudioProActionsEnabled: true,
        MaiaIntegrationEnabled: true,
        RefreshFromDiskHotkey: "F4",
        RestoreTabsOnReopen: true,
        SkillsEnabled: true,
        SkillClients: new[] { "claude", "copilot" });

    private const string FileName = "terminal-settings.json";
    private const string SubDir = "resources";

    private static string DefaultShellPath()
    {
        if (OperatingSystem.IsWindows()) return "powershell.exe";
        // Prefer the user's login shell on POSIX. Fall back to /bin/zsh
        // (macOS default since Catalina) or /bin/sh (POSIX-mandated).
        var loginShell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(loginShell) && File.Exists(loginShell)) return loginShell;
        if (File.Exists("/bin/zsh")) return "/bin/zsh";
        return "/bin/sh";
    }

    /// <summary>
    /// If the persisted shell path is obviously incompatible with the current
    /// OS (e.g. <c>cmd.exe</c> after the project moves from a Windows dev box
    /// to a Mac one), swap it for the OS-aware default. A bare command name
    /// without a path separator (<c>bash</c>, <c>zsh</c>) passes through and
    /// will be resolved against PATH at spawn time.
    /// </summary>
    internal static string MigrateShellPathForPlatform(string saved)
    {
        if (string.IsNullOrEmpty(saved)) return DefaultShellPath();
        bool looksWindows = saved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                          || saved.Contains('\\');
        bool looksUnix = saved.StartsWith("/");
        if (OperatingSystem.IsWindows() && looksUnix) return DefaultShellPath();
        if (!OperatingSystem.IsWindows() && looksWindows) return DefaultShellPath();
        return saved;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static string PathFor(string projectDir) =>
        System.IO.Path.Combine(projectDir, SubDir, FileName);

    public static TerminalSettings Load(string projectDir)
    {
        var path = PathFor(projectDir);
        if (!File.Exists(path)) return Defaults();
        try
        {
            using var stream = File.OpenRead(path);
            var dto = JsonSerializer.Deserialize<Dto>(stream, Json);
            if (dto is null) return Defaults();
            var def = Defaults();
            // Migration: old key "actionsServerEnabled" → new key "mcpServerEnabled".
            // Old key "actionsServerPort" → new key "mcpServerPort". If both old
            // and new are present, new wins. Sub-toggles default true so an old
            // settings file that just had the master flag opts into both
            // tool families on first load.
            bool master = dto.McpServerEnabled ?? dto.ActionsServerEnabled ?? def.McpServerEnabled;
            int port = dto.McpServerPort ?? dto.ActionsServerPort ?? def.McpServerPort;
            return new TerminalSettings(
                ShellPath: MigrateShellPathForPlatform(dto.ShellPath ?? def.ShellPath),
                Args: dto.Args ?? def.Args,
                RingBufferKB: dto.RingBufferKB ?? def.RingBufferKB,
                XtermScrollbackLines: dto.XtermScrollbackLines ?? def.XtermScrollbackLines,
                Theme: dto.Theme ?? def.Theme,
                McpEnabled: dto.McpEnabled ?? def.McpEnabled,
                McpPort: dto.McpPort ?? def.McpPort,
                McpClients: dto.McpClients ?? def.McpClients,
                McpServerEnabled: master,
                McpServerPort: port,
                StudioProActionsEnabled: dto.StudioProActionsEnabled ?? def.StudioProActionsEnabled,
                MaiaIntegrationEnabled: dto.MaiaIntegrationEnabled ?? def.MaiaIntegrationEnabled,
                RefreshFromDiskHotkey: dto.RefreshFromDiskHotkey ?? def.RefreshFromDiskHotkey,
                RestoreTabsOnReopen: dto.RestoreTabsOnReopen ?? def.RestoreTabsOnReopen,
                SkillsEnabled: dto.SkillsEnabled ?? def.SkillsEnabled,
                SkillClients: dto.SkillClients ?? def.SkillClients);
        }
        catch (JsonException)
        {
            return Defaults();
        }
    }

    public void Save(string projectDir)
    {
        var dir = System.IO.Path.Combine(projectDir, SubDir);
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, FileName);
        var dto = new Dto(
            ShellPath, Args, RingBufferKB, XtermScrollbackLines, Theme,
            McpEnabled, McpPort, McpClients,
            McpServerEnabled, McpServerPort,
            StudioProActionsEnabled, MaiaIntegrationEnabled,
            RefreshFromDiskHotkey, RestoreTabsOnReopen,
            SkillsEnabled, SkillClients,
            // Legacy keys: write them too so an older Concord build that reads
            // this file keeps the master toggle in sync. Drop after 1.4.0.
            ActionsServerEnabled: McpServerEnabled,
            ActionsServerPort: McpServerPort);
        File.WriteAllText(path, JsonSerializer.Serialize(dto, Json));
    }

    private sealed record Dto(
        string? ShellPath,
        string[]? Args,
        int? RingBufferKB,
        int? XtermScrollbackLines,
        string? Theme,
        bool? McpEnabled,
        int? McpPort,
        string[]? McpClients,
        bool? McpServerEnabled,
        int? McpServerPort,
        bool? StudioProActionsEnabled,
        bool? MaiaIntegrationEnabled,
        string? RefreshFromDiskHotkey,
        bool? RestoreTabsOnReopen,
        bool? SkillsEnabled,
        string[]? SkillClients,
        bool? ActionsServerEnabled = null,
        int? ActionsServerPort = null);
}
