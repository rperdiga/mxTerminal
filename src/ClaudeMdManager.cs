namespace Terminal;

/// <summary>
/// Manages a fenced block in <c>&lt;project&gt;/CLAUDE.md</c> that auto-imports
/// Concord's rules so Claude Code loads them on every session.
/// <para>
/// The block looks like:
/// <code>
/// &lt;!-- BEGIN CONCORD MANAGED -- regenerated on every Save; do not edit. --&gt;
/// &lt;!-- For project-specific rules, drop .md files into .claude/rules/project/ --&gt;
///
/// @.claude/rules/concord-build-rules.md
///
/// @.claude/rules/project/README.md
/// @.claude/rules/project/payment-conventions.md
///
/// &lt;!-- END CONCORD MANAGED --&gt;
/// </code>
/// </para>
/// <para>
/// Behavior:
/// <list type="bullet">
/// <item>If <c>CLAUDE.md</c> doesn't exist: created with just the block.</item>
/// <item>If it exists with exactly one managed block: contents inside the
/// markers are regenerated <b>in place</b>; everything above and below is
/// preserved verbatim, including order. The block does not migrate to the
/// top of the file on Save.</item>
/// <item>If it exists without the block: the block is prepended to the top
/// of the file, separated from existing content by a blank line. The first
/// time this happens, leading blank lines on the existing file are trimmed
/// so the file doesn't accumulate empty space on the boundary.</item>
/// <item>If multiple corrupt-state managed blocks exist: collapsed to one,
/// positioned at the top (deterministic recovery from a corrupt state).</item>
/// <item>If a literal BEGIN marker exists with no matching END (orphan from
/// a manual paste or crash-mid-write): the orphan content is <b>preserved</b>;
/// a fresh managed block is prepended above. The orphan is left as-is for
/// the user to inspect or clean up.</item>
/// <item>The Concord-managed canonical file (<c>concord-build-rules.md</c>) is
/// listed first; <c>project/**/*.md</c> entries follow in sorted order.</item>
/// <item>Writes are atomic (write-temp-then-move) so an interrupted Save
/// can never leave a half-written <c>CLAUDE.md</c>.</item>
/// </list>
/// </para>
/// <para>
/// Path semantics: imports are written with forward slashes
/// (<c>@.claude/rules/concord-build-rules.md</c>) for portability across
/// Windows / macOS / Linux. Claude Code accepts both separators on Windows.
/// </para>
/// </summary>
public sealed class ClaudeMdManager
{
    public const string BeginMarker = "<!-- BEGIN CONCORD MANAGED -->";
    public const string EndMarker = "<!-- END CONCORD MANAGED -->";
    public const string ClaudeMdFileName = "CLAUDE.md";

    private readonly string projectDir;
    private readonly string rulesSubdir;
    private readonly Logger log;

    /// <summary>
    /// Construct a manager scoped to one project's <c>CLAUDE.md</c>.
    /// </summary>
    /// <param name="rulesSubdir">Project-relative path to the rules directory
    /// (typically <c>.claude/rules</c>). The manager scans this folder for
    /// the canonical file plus the <c>project/</c> subfolder.</param>
    public ClaudeMdManager(string projectDir, string rulesSubdir, Logger log)
    {
        this.projectDir = projectDir;
        this.rulesSubdir = NormalizeSubdir(rulesSubdir);
        this.log = log;
    }

    /// <summary>
    /// Write or refresh the managed block in <c>CLAUDE.md</c>. Block contents
    /// are regenerated from current disk state — the canonical Concord file if
    /// present, plus every <c>.md</c> in the <c>project/</c> subfolder
    /// (recursive, sorted). Idempotent: the same disk state produces the same
    /// CLAUDE.md.
    /// </summary>
    public void Apply()
    {
        var claudeMdPath = Path.Combine(projectDir, ClaudeMdFileName);
        var block = BuildBlock();

        // Nothing to import → nothing to manage. If a previous block exists,
        // strip it; otherwise leave CLAUDE.md alone.
        if (block.Length == 0)
        {
            if (File.Exists(claudeMdPath))
            {
                var existingBody = File.ReadAllText(claudeMdPath);
                var stripped = StripManagedBlocks(existingBody);
                if (!ReferenceEquals(stripped, existingBody))
                {
                    AtomicWrite(claudeMdPath, stripped);
                    log.Info($"[claude-md] no rules to manage; stripped existing block in {claudeMdPath}");
                }
            }
            return;
        }

        if (!File.Exists(claudeMdPath))
        {
            AtomicWrite(claudeMdPath, block + "\n");
            log.Info($"[claude-md] created {claudeMdPath} with managed block");
            return;
        }

        var body = File.ReadAllText(claudeMdPath);
        var newBody = ReplaceOrPrependBlock(body, block);
        if (newBody != body)
        {
            AtomicWrite(claudeMdPath, newBody);
            log.Info($"[claude-md] refreshed managed block in {claudeMdPath}");
        }
    }

    /// <summary>
    /// Strip the managed block (and any duplicate corrupt-state blocks) from
    /// <c>CLAUDE.md</c>. If the file becomes empty after stripping, the file
    /// itself is left in place (empty) — we never silently delete a file the
    /// user might want to keep.
    /// </summary>
    public void Remove()
    {
        var claudeMdPath = Path.Combine(projectDir, ClaudeMdFileName);
        if (!File.Exists(claudeMdPath)) return;

        var body = File.ReadAllText(claudeMdPath);
        var stripped = StripManagedBlocks(body);
        if (!ReferenceEquals(stripped, body))
        {
            AtomicWrite(claudeMdPath, stripped);
            log.Info($"[claude-md] removed managed block from {claudeMdPath}");
        }
    }

    /// <summary>
    /// Build the import block from current rules-folder state. Returns an
    /// empty string when there is nothing to import (no canonical file AND
    /// no project/ files).
    /// </summary>
    private string BuildBlock()
    {
        var rulesAbsolute = Path.Combine(projectDir, rulesSubdir);
        var rulesRelative = rulesSubdir.Replace(Path.DirectorySeparatorChar, '/');

        var imports = new List<string>();

        // Canonical Concord-managed file first.
        var canonicalAbs = Path.Combine(rulesAbsolute, RulesInstaller.CanonicalFileName);
        if (File.Exists(canonicalAbs))
        {
            imports.Add($"@{rulesRelative}/{RulesInstaller.CanonicalFileName}");
        }

        // Project-specific imports follow, sorted (deterministic regen).
        var projectFolder = Path.Combine(rulesAbsolute, RulesInstaller.ProjectFolderName);
        if (Directory.Exists(projectFolder))
        {
            var projectFiles = Directory.EnumerateFiles(projectFolder, "*.md", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var p in projectFiles)
            {
                var relFromProject = Path.GetRelativePath(rulesAbsolute, p)
                    .Replace(Path.DirectorySeparatorChar, '/');
                imports.Add($"@{rulesRelative}/{relFromProject}");
            }
        }

        if (imports.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.Append(BeginMarker).Append('\n');
        sb.Append("<!-- Regenerated on every Concord Save. Do not edit between markers — your changes will be overwritten. -->").Append('\n');
        sb.Append("<!-- For project-specific rules, drop .md files into ").Append(rulesRelative).Append('/').Append(RulesInstaller.ProjectFolderName).Append("/ — they are auto-imported below. -->").Append('\n');
        sb.Append('\n');
        foreach (var import in imports)
        {
            sb.Append(import).Append('\n');
        }
        sb.Append('\n');
        sb.Append(EndMarker);
        return sb.ToString();
    }

    /// <summary>
    /// Replace the existing managed block in-place (preserving content above
    /// and below) when exactly one well-formed block exists and no orphans.
    /// Prepend a fresh block when no managed marker exists at all. When
    /// multiple BEGIN markers exist (corrupt state, possibly mixing
    /// well-formed blocks and orphans), use <see cref="StripManagedBlocks"/>
    /// to remove well-formed blocks while preserving orphan content, then
    /// prepend a fresh block at the top. A single orphan BEGIN with no
    /// matching END anywhere is preserved as-is; fresh block prepends above.
    /// </summary>
    private static string ReplaceOrPrependBlock(string body, string newBlock)
    {
        var firstBegin = body.IndexOf(BeginMarker, StringComparison.Ordinal);
        if (firstBegin < 0)
        {
            // No managed block exists — prepend at top, separated by a blank line.
            var trimmed = body.TrimStart('\r', '\n', ' ', '\t');
            if (trimmed.Length == 0) return newBlock + "\n";
            return newBlock + "\n\n" + trimmed;
        }

        // If multiple BEGIN markers exist anywhere in the body, fall through
        // to the strip-and-prepend path. StripManagedBlocks correctly removes
        // well-formed blocks while preserving orphan-BEGIN content (so we
        // don't silently destroy intervening user content between an orphan
        // BEGIN and a later well-formed block).
        var secondBegin = body.IndexOf(BeginMarker, firstBegin + BeginMarker.Length, StringComparison.Ordinal);
        if (secondBegin >= 0)
        {
            var stripped = StripManagedBlocks(body);
            var trimmed = stripped.TrimStart('\r', '\n', ' ', '\t');
            if (trimmed.Length == 0) return newBlock + "\n";
            return newBlock + "\n\n" + trimmed;
        }

        // Exactly one BEGIN. Find its matching END (no intervening BEGIN to
        // worry about — there's only one BEGIN total).
        var firstEnd = body.IndexOf(EndMarker, firstBegin + BeginMarker.Length, StringComparison.Ordinal);
        if (firstEnd < 0)
        {
            // Single orphan BEGIN with no matching END — preserve as user
            // content; prepend fresh block above it.
            return newBlock + "\n\n" + body;
        }

        // Exactly one well-formed block — replace in place, preserving the
        // exact byte positioning of surrounding content.
        var afterBlock = firstEnd + EndMarker.Length;
        return body.Substring(0, firstBegin) + newBlock + body.Substring(afterBlock);
    }

    /// <summary>
    /// Determine the END marker that matches the BEGIN at <paramref name="beginIdx"/>.
    /// Returns the END index if a well-formed block exists; returns -1 if the
    /// BEGIN is orphan — either no END exists at all, OR another BEGIN appears
    /// between this one and the next END (which makes the first BEGIN orphan
    /// and the second BEGIN the start of a different block).
    /// </summary>
    private static int FindMatchingEnd(string body, int beginIdx)
    {
        var endIdx = body.IndexOf(EndMarker, beginIdx + BeginMarker.Length, StringComparison.Ordinal);
        if (endIdx < 0) return -1;
        var nextBegin = body.IndexOf(BeginMarker, beginIdx + BeginMarker.Length, StringComparison.Ordinal);
        if (nextBegin >= 0 && nextBegin < endIdx) return -1;
        return endIdx;
    }

    /// <summary>
    /// Remove every well-formed <c>BEGIN CONCORD MANAGED</c>...<c>END CONCORD
    /// MANAGED</c> block from <paramref name="body"/>. Tolerant of multiple
    /// blocks (corrupt state) and of orphan BEGIN markers in any position.
    /// <para>
    /// An orphan BEGIN (no END, OR another BEGIN appears before the next END)
    /// is treated as user content and preserved verbatim — silent truncation
    /// would be data loss with no warning. When an orphan BEGIN is followed
    /// later by a well-formed block, only the well-formed block is stripped;
    /// the orphan + any intervening user content survives.
    /// </para>
    /// <para>
    /// Returns the original string instance when no well-formed block was
    /// stripped (so callers can detect "nothing changed" via reference
    /// equality, even if orphan content was traversed).
    /// </para>
    /// </summary>
    private static string StripManagedBlocks(string body)
    {
        if (!body.Contains(BeginMarker, StringComparison.Ordinal)) return body;

        var sb = new System.Text.StringBuilder(body.Length);
        var cursor = 0;
        var stripped = false;
        while (cursor < body.Length)
        {
            var beginIdx = body.IndexOf(BeginMarker, cursor, StringComparison.Ordinal);
            if (beginIdx < 0)
            {
                sb.Append(body, cursor, body.Length - cursor);
                break;
            }
            // Append everything up to the BEGIN marker.
            sb.Append(body, cursor, beginIdx - cursor);

            // Determine matching END (returns -1 if orphan: no END, or an
            // intervening BEGIN before the next END).
            var endIdx = FindMatchingEnd(body, beginIdx);
            if (endIdx < 0)
            {
                // Orphan BEGIN. Two sub-cases:
                //   (a) No END exists at all  → preserve from BEGIN to EOF.
                //   (b) Another BEGIN appears before the next END → preserve
                //       from this BEGIN up to (but not including) the next
                //       BEGIN, then resume scanning at that next BEGIN.
                var nextBegin = body.IndexOf(BeginMarker, beginIdx + BeginMarker.Length, StringComparison.Ordinal);
                if (nextBegin < 0)
                {
                    // (a) Append BEGIN→EOF as user content and stop.
                    sb.Append(body, beginIdx, body.Length - beginIdx);
                    cursor = body.Length;
                    break;
                }
                // (b) Append orphan BEGIN + intervening content; resume at
                //     the next BEGIN candidate.
                sb.Append(body, beginIdx, nextBegin - beginIdx);
                cursor = nextBegin;
                continue;
            }

            stripped = true;
            cursor = endIdx + EndMarker.Length;

            // Also consume trailing newline(s) immediately after END marker so
            // we don't leave empty lines where blocks used to be.
            while (cursor < body.Length && (body[cursor] == '\n' || body[cursor] == '\r'))
            {
                cursor++;
            }
        }

        // If we never actually stripped a well-formed block (only orphan
        // content was traversed), return the original string instance so
        // callers can detect "nothing changed" via reference equality.
        return stripped ? sb.ToString() : body;
    }

    /// <summary>
    /// Atomic write — write to a sibling temp file, then rename over the
    /// destination. An interrupted process can leave the temp file behind, but
    /// never a half-written CLAUDE.md.
    /// </summary>
    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".concord.tmp";
        File.WriteAllText(tmp, content);
        try
        {
            // File.Move with overwrite is atomic on the same filesystem.
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Best-effort cleanup of the temp file on failure.
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }

    private static string NormalizeSubdir(string sub) =>
        sub.Replace('/', Path.DirectorySeparatorChar)
           .Replace('\\', Path.DirectorySeparatorChar);
}
