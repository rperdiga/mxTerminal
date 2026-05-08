namespace Terminal.Messages;

public sealed record SessionInfoPayload(
    string TabId,
    string Title,
    string ShellPath,
    string Cwd,
    bool Alive);

public sealed record TabsListPayload(IReadOnlyList<SessionInfoPayload> Tabs);

public sealed record TabCreatedPayload(string TabId, string Title, string ShellPath, string Cwd);

public sealed record TabClosedPayload(string TabId);

public sealed record OutputPayload(string TabId, string DataB64);

public sealed record ExitPayload(string TabId, int? ExitCode = null, string? Signal = null);

public sealed record ReplayDataPayload(string TabId, string DataB64);

public sealed record ShellOptionPayload(string Name, string Path);

public sealed record SettingsPayload(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines,
    string Theme,
    IReadOnlyList<ShellOptionPayload> AvailableShells,
    bool McpEnabled,
    int McpPort,
    string[] McpClients,
    bool McpServerEnabled,
    int McpServerPort,
    bool StudioProActionsEnabled,
    bool MaiaIntegrationEnabled,
    string Platform,
    string RefreshFromDiskHotkey,
    bool RestoreTabsOnReopen,
    AboutInfoPayload About,
    StudioProMcpInfoPayload? StudioProMcp);

/// <summary>
/// Snapshot of Studio Pro's own MCP-server preference. JS uses this to warn
/// when the port we wire into Claude's .mcp.json doesn't match what Studio
/// Pro actually serves on. Null when the SQLite probe couldn't read it.
/// </summary>
public sealed record StudioProMcpInfoPayload(bool? Enabled, int? Port);

/// <summary>Read-only metadata shown in the settings modal's About section.</summary>
public sealed record AboutInfoPayload(
    string Version,
    string? LogPath,
    string? SettingsPath);

/// <summary>
/// Sent on save when MCP-related work succeeded or failed. The JS side
/// shows a banner. If <see cref="Ok"/> is false, the saveSettings call
/// did NOT persist (so the toggle stays in its previous state).
/// </summary>
public sealed record McpResultPayload(bool Ok, string Message, string[] Touched);

public sealed record ErrorPayload(string Message, string? Context = null);
