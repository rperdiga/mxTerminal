namespace Terminal;

/// <summary>
/// Codex sibling of <see cref="ClaudeMdManager"/>: manages the same
/// fenced "BEGIN/END CONCORD MANAGED" block, but in
/// <c>&lt;project&gt;/AGENTS.md</c> with imports against the Codex rules
/// subdir (typically <c>.codex/rules</c>).
/// <para>
/// Codex AGENTS.md uses the same <c>@</c>-import syntax Claude Code does
/// for referencing sibling markdown files. The block format and orphan-
/// preservation semantics are identical to <see cref="ClaudeMdManager"/>;
/// the only override is the destination file path.
/// </para>
/// </summary>
public sealed class AgentsMdManager : ClaudeMdManager
{
    public const string AgentsMdFileName = "AGENTS.md";

    protected override string ManagedFilePath => AgentsMdFileName;
    protected override string LogPrefix => "agents-md";

    public AgentsMdManager(string projectDir, string rulesSubdir, Logger log)
        : base(projectDir, rulesSubdir, log) { }
}
