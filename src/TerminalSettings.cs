using System.Text.Json;
using System.Text.Json.Serialization;

namespace Terminal;

public sealed record TerminalSettings(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines,
    string Theme,
    bool McpEnabled,
    string[] McpClients,
    bool McpServerEnabled,
    bool StudioProActionsEnabled,
    bool MaiaIntegrationEnabled,
    string RefreshFromDiskHotkey,
    bool RestoreTabsOnReopen,
    bool SkillsEnabled,
    string[] SkillClients,
    // Stamps the Concord version that last ran the apply-defaults chain
    // against this project. Null on fresh-defaults / files written by
    // pre-4.1.1 Concord. Compared against the current assembly version
    // by TerminalPaneExtension.TryUpgradeApply to decide whether to
    // re-default the wiring keys (MCP + skills + sub-toggles) on first
    // open after an upgrade. Default value lets all existing positional
    // constructors compile unchanged.
    string? LastAppliedVersion = null)
{
    /// <summary>
    /// Studio Pro's standard MCP-server port (Mendix default in Edit →
    /// Preferences → Maia → MCP Server). The runtime always re-probes
    /// Studio Pro's actual port from <c>%LOCALAPPDATA%\Mendix\Settings.sqlite</c>
    /// at save time — this constant is the fallback only when the probe
    /// fails (e.g. SQLite locked, Studio Pro not yet launched).
    /// </summary>
    public const int StudioProMcpDefaultPort = 8100;

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
        McpClients: new[] { "claude", "copilot" },
        McpServerEnabled: true,
        StudioProActionsEnabled: true,
        MaiaIntegrationEnabled: true,
        RefreshFromDiskHotkey: "F4",
        RestoreTabsOnReopen: true,
        SkillsEnabled: true,
        SkillClients: new[] { "claude", "copilot" },
        // Defaults() is the in-memory "no file" representation; the
        // version stamp is written to disk by the apply paths
        // (TryFirstRunApply / TryUpgradeApply) once they've actually
        // materialized state, not by Defaults() itself.
        LastAppliedVersion: null);

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
        WriteIndented = true,
        // Skip null fields when serializing — keeps the saved file clean.
        // Otherwise legacy back-compat keys like ActionsServerEnabled
        // surface as `"actionsServerEnabled": null` noise on every save.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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
            // Sub-toggles default true so an old settings file that just had
            // the master flag opts into both tool families on first load.
            // (Old port keys mcpServerPort / actionsServerPort / mcpPort are
            // silently dropped on next save — the runtime never read them.)
            bool master = dto.McpServerEnabled ?? dto.ActionsServerEnabled ?? def.McpServerEnabled;
            return new TerminalSettings(
                ShellPath: MigrateShellPathForPlatform(dto.ShellPath ?? def.ShellPath),
                Args: dto.Args ?? def.Args,
                RingBufferKB: dto.RingBufferKB ?? def.RingBufferKB,
                XtermScrollbackLines: dto.XtermScrollbackLines ?? def.XtermScrollbackLines,
                Theme: dto.Theme ?? def.Theme,
                McpEnabled: dto.McpEnabled ?? def.McpEnabled,
                McpClients: dto.McpClients ?? def.McpClients,
                McpServerEnabled: master,
                StudioProActionsEnabled: dto.StudioProActionsEnabled ?? def.StudioProActionsEnabled,
                MaiaIntegrationEnabled: dto.MaiaIntegrationEnabled ?? def.MaiaIntegrationEnabled,
                RefreshFromDiskHotkey: dto.RefreshFromDiskHotkey ?? def.RefreshFromDiskHotkey,
                RestoreTabsOnReopen: dto.RestoreTabsOnReopen ?? def.RestoreTabsOnReopen,
                SkillsEnabled: dto.SkillsEnabled ?? def.SkillsEnabled,
                SkillClients: dto.SkillClients ?? def.SkillClients,
                // Pass through verbatim — null is meaningful (signals
                // "this settings file pre-dates upgrade-apply tracking"
                // to TryUpgradeApply, which then runs against it).
                LastAppliedVersion: dto.LastAppliedVersion);
        }
        catch (JsonException)
        {
            // The user's settings file is corrupt. Don't silently default —
            // back it up so the user can recover their custom shell/theme/etc.,
            // then return defaults so the pane keeps working.
            try
            {
                var backup = path + ".broken-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".bak";
                File.Move(path, backup);
            }
            catch { /* best-effort */ }
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
            McpEnabled, McpClients,
            McpServerEnabled,
            StudioProActionsEnabled, MaiaIntegrationEnabled,
            RefreshFromDiskHotkey, RestoreTabsOnReopen,
            SkillsEnabled, SkillClients,
            LastAppliedVersion: LastAppliedVersion);
        // Atomic write: stage to .tmp, then File.Replace for journaled
        // rename on NTFS so a crash mid-save can never leave an empty file.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(dto, Json));
        if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else File.Move(tmp, path);
    }

    // The DTO has fewer fields than TerminalSettings: McpPort, McpServerPort,
    // and ActionsServerPort were removed in v4.1.2 (they were persisted but
    // ignored at runtime — the runtime always probes Studio Pro's live port
    // for the primary MCP, and binds 7783 with auto-fallback for Concord MCP).
    // Older settings files containing those keys deserialize fine — JSON
    // ignores fields the DTO doesn't declare — and the next Save drops them.
    // ActionsServerEnabled is kept ONLY for back-compat reads from pre-1.3.0
    // files that used the legacy master-flag name; never written.
    private sealed record Dto(
        string? ShellPath,
        string[]? Args,
        int? RingBufferKB,
        int? XtermScrollbackLines,
        string? Theme,
        bool? McpEnabled,
        string[]? McpClients,
        bool? McpServerEnabled,
        bool? StudioProActionsEnabled,
        bool? MaiaIntegrationEnabled,
        string? RefreshFromDiskHotkey,
        bool? RestoreTabsOnReopen,
        bool? SkillsEnabled,
        string[]? SkillClients,
        string? LastAppliedVersion = null,
        // Legacy back-compat reads only — never written:
        bool? ActionsServerEnabled = null);
}
