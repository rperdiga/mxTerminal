namespace MxStudioProTerminal;

public sealed record SessionInfo(string TabId, string Title, string ShellPath, string Cwd, bool Alive);
