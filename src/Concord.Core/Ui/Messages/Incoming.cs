namespace Terminal.Messages;

public sealed record CreateTabPayload(
    int Cols,
    int Rows,
    string? ShellPath = null,
    string[]? Args = null,
    string? Cwd = null);

public sealed record CloseTabPayload(string TabId);

public sealed record InputPayload(string TabId, string DataB64);

public sealed record ResizePayload(string TabId, int Cols, int Rows);

public sealed record ReplayPayload(string TabId);

public sealed record SaveSettingsPayload(
    string ShellPath,
    string[] Args,
    int? RingBufferKB = null,
    int? XtermScrollbackLines = null,
    string? Theme = null,
    bool? McpEnabled = null,
    string[]? McpClients = null,
    bool? McpServerEnabled = null,
    bool? StudioProActionsEnabled = null,
    bool? MaiaIntegrationEnabled = null,
    bool? MaiaDiagnosticLogging = null,
    string? RefreshFromDiskHotkey = null,
    bool? RestoreTabsOnReopen = null,
    bool? SkillsEnabled = null,
    string[]? SkillClients = null);
