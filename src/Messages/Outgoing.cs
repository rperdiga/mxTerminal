namespace MxStudioProTerminal.Messages;

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

public sealed record SettingsPayload(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines);

public sealed record ErrorPayload(string Message, string? Context = null);
