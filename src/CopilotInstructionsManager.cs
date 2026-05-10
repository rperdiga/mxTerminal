namespace Terminal;

/// <summary>
/// GitHub Copilot CLI sibling of <see cref="ClaudeMdManager"/>: manages
/// the same fenced "BEGIN/END CONCORD MANAGED" block, but in
/// <c>&lt;project&gt;/.github/copilot-instructions.md</c> with imports
/// against the Copilot rules subdir (typically <c>.github/rules</c>).
/// <para>
/// Copilot CLI's <c>copilot-instructions.md</c> supports the same
/// <c>@</c>-import directive Claude Code uses, so the block contents are
/// identical to AGENTS.md / CLAUDE.md modulo the rules-folder path. The
/// destination file is one directory below project root; the base class
/// handles the <c>.github/</c> directory creation when the block is
/// applied for the first time.
/// </para>
/// </summary>
public sealed class CopilotInstructionsManager : ClaudeMdManager
{
    public const string CopilotInstructionsFileName = ".github/copilot-instructions.md";

    protected override string ManagedFilePath => CopilotInstructionsFileName;
    protected override string LogPrefix => "copilot-instructions";

    public CopilotInstructionsManager(string projectDir, string rulesSubdir, Logger log)
        : base(projectDir, rulesSubdir, log) { }
}
