using System.Text;

namespace Terminal;

/// <summary>
/// Manages the <c>[mcp_servers.&lt;name&gt;]</c> sections of the user-level
/// Codex config at <c>~/.codex/config.toml</c>. Codex 0.128+ natively supports
/// streamable HTTP transport — we just write <c>url = "..."</c> directly,
/// no stdio bridge. Earlier versions of this configurator used the npx
/// <c>mcp-remote</c> bridge, which was both fragile (depended on Node + a
/// healthy npm cache) and unnecessary now.
///
/// Hand-rolled TOML editing — no need for a full parser since we own a fixed
/// set of well-known sections and never touch anything else.
/// </summary>
public sealed class McpTomlConfigurator
{
    public const string ServerName        = "mendix-studio-pro";
    public const string ActionsServerName = "concord-mcp";

    /// <summary>
    /// Pre-v1.3.0 section name. <see cref="UpsertActions"/> and <see cref="RemoveActions"/>
    /// transparently strip this on every save so user configs migrate without manual edits.
    /// </summary>
    private const string LegacyActionsServerName = "mendix-studio-pro-actions";

    private static string HeaderFor(string serverName) => $"[mcp_servers.{serverName}]";

    private readonly string filePath;

    public McpTomlConfigurator() : this(DefaultPath()) { }

    internal McpTomlConfigurator(string filePath) { this.filePath = filePath; }

    private static string DefaultPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".codex", "config.toml");
    }

    public string FilePath => filePath;

    public void Upsert(string url) => UpsertNamed(ServerName, url);
    public void Remove()           => RemoveNamed(ServerName);

    public void UpsertActions(string url)
    {
        RemoveNamed(LegacyActionsServerName);
        UpsertNamed(ActionsServerName, url);
    }

    public void RemoveActions()
    {
        RemoveNamed(LegacyActionsServerName);
        RemoveNamed(ActionsServerName);
    }

    /// <summary>
    /// v4.2.2: suppress Codex 0.128+'s "External agent config detected"
    /// migration prompt for a specific project that Concord is wiring.
    /// The prompt offers to migrate Claude Code config across to Codex —
    /// MCP servers, subagents, plugins, recent sessions. For users who
    /// installed Concord, Codex is already configured by Concord, so the
    /// prompt is noise rather than value. Codex throttles re-prompting via
    /// a per-project unix-epoch stamp under
    /// <c>[notice.external_config_migration_prompts.project_last_prompted_at]</c>;
    /// writing a far-future stamp effectively suppresses the prompt without
    /// disabling the underlying feature for other (non-Concord-managed)
    /// projects.
    /// <para>
    /// Surgical by design: only writes the per-project stamp for the
    /// project Concord is currently wiring. Does NOT touch the home-level
    /// stamp (so the user still sees the prompt the first time they enter
    /// a non-Concord-managed project, where it may be genuinely useful).
    /// Does NOT flip the <c>[features] external_migration</c> master toggle.
    /// </para>
    /// <para>
    /// Idempotent: if an entry already exists for this project with any
    /// value, the stamp is updated to <see cref="SuppressionEpoch"/>.
    /// If the existing value is already greater than or equal to
    /// <see cref="SuppressionEpoch"/>, no-op (avoids touching files
    /// gratuitously on every Concord apply).
    /// </para>
    /// </summary>
    public void SuppressMigrationPromptForProject(string projectDir)
    {
        if (string.IsNullOrEmpty(projectDir)) return;
        // Use the project dir as-given. Codex's own writes preserve the
        // exact case the user typed; matching that means our stamp wins
        // against any Codex-written prior stamp for the same project.
        var normalizedKey = projectDir.TrimEnd(Path.DirectorySeparatorChar, '/');

        var lines = ReadLines();

        // Locate (or create) the nested table:
        //   [notice.external_config_migration_prompts.project_last_prompted_at]
        const string SubTableHeader = "[notice.external_config_migration_prompts.project_last_prompted_at]";

        var (subStart, subEnd) = FindSection(lines, SubTableHeader);
        if (subStart < 0)
        {
            // Append a fresh table at the end.
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add(SubTableHeader);
            subStart = lines.Count - 1;
            subEnd = subStart;
            lines.Add(FormatStampLine(normalizedKey));
            WriteAtomic(lines);
            return;
        }

        // Scan the existing table for an entry matching this project.
        // TOML allows three quoting styles for keys with special chars:
        //   'literal'    — single-quoted, backslashes literal (Codex's choice)
        //   "basic"      — double-quoted, backslashes need escaping
        //   bare         — only if the key is plain identifier (won't be the case for paths)
        // We write single-quoted (matches what Codex writes for Windows paths).
        var literalQuoted = $"'{normalizedKey}'";
        int existingEntryIdx = -1;
        for (int i = subStart + 1; i <= subEnd && i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith(literalQuoted + " ", StringComparison.Ordinal) ||
                trimmed.StartsWith(literalQuoted + "=", StringComparison.Ordinal))
            {
                existingEntryIdx = i;
                break;
            }
        }

        if (existingEntryIdx >= 0)
        {
            // Update in place — but skip if current value is already >= SuppressionEpoch
            // (avoid gratuitous file writes on every Concord apply).
            var current = lines[existingEntryIdx];
            var eqIdx = current.IndexOf('=');
            if (eqIdx > 0)
            {
                var rhs = current.Substring(eqIdx + 1).Trim();
                // Strip trailing comments if any.
                var hashIdx = rhs.IndexOf('#');
                if (hashIdx >= 0) rhs = rhs.Substring(0, hashIdx).Trim();
                if (long.TryParse(rhs, out var existing) && existing >= SuppressionEpoch) return;
            }
            lines[existingEntryIdx] = FormatStampLine(normalizedKey);
        }
        else
        {
            // Insert after the last entry of the sub-table.
            lines.Insert(subEnd + 1, FormatStampLine(normalizedKey));
        }

        WriteAtomic(lines);
    }

    /// <summary>
    /// Far-future unix epoch (year 2099 = 4070908800). Codex's prompt
    /// throttle is timestamp-based; this value puts the next eligible
    /// prompt 70+ years from now, effectively a "don't ask again" stamp
    /// while remaining a syntactically plausible unix timestamp.
    /// </summary>
    private const long SuppressionEpoch = 4070908800L;

    private static string FormatStampLine(string projectKey) =>
        // Trailing comment is TOML-legal and matches Concord's hygiene contract
        // (every Concord-written artifact carries a managed marker so users can
        // discover what wrote a given line; cf. CLAUDE.md / AGENTS.md fenced blocks).
        $"'{projectKey}' = {SuppressionEpoch}   # concord-managed: migration-prompt suppression";

    private void UpsertNamed(string serverName, string url)
    {
        var header = HeaderFor(serverName);
        var lines = ReadLines();
        var (start, end) = FindSection(lines, header);
        var newSection = new[]
        {
            header,
            $"url = \"{url}\"",
        };

        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.AddRange(newSection);
        }
        else
        {
            lines.RemoveRange(start, end - start + 1);
            lines.InsertRange(start, newSection);
        }
        WriteAtomic(lines);
    }

    private void RemoveNamed(string serverName)
    {
        if (!File.Exists(filePath)) return;
        var lines = ReadLines();
        // v4.2.2: strip the parent section AND every child sub-section
        // (e.g. [mcp_servers.<name>.tools.<x>]) in one pass. Pre-v1.3.0
        // Concord installs wrote per-tool sub-sections under the parent;
        // earlier RemoveNamed only stripped the parent header, leaving
        // orphan sub-sections. Older Codex tolerated them; Codex 0.128+
        // refuses to start with `invalid transport in mcp_servers.<name>`
        // because the sub-sections imply a server with no url/command.
        // See capture file item #1 for the empirical surface that caused
        // a hard-block on Neo's first v4.2.1 Codex run.
        var parentHeader = HeaderFor(serverName);
        var childPrefix = $"[mcp_servers.{serverName}.";
        bool changed = false;

        while (true)
        {
            int found = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].TrimStart();
                bool isParent = t == parentHeader;
                bool isChild = t.StartsWith(childPrefix, StringComparison.Ordinal)
                                && t.EndsWith("]", StringComparison.Ordinal);
                if (isParent || isChild)
                {
                    found = i;
                    break;
                }
            }
            if (found < 0) break;

            // Find end of this section (next [ line or EOF).
            int end = lines.Count - 1;
            for (int j = found + 1; j < lines.Count; j++)
            {
                if (lines[j].TrimStart().StartsWith("[", StringComparison.Ordinal))
                {
                    end = j - 1;
                    break;
                }
            }

            var until = end;
            if (until + 1 < lines.Count && lines[until + 1].Trim().Length == 0) until++;
            var from = found;
            if (from > 0 && lines[from - 1].Trim().Length == 0) from--;

            lines.RemoveRange(from, until - from + 1);
            changed = true;
        }

        if (changed) WriteAtomic(lines);
    }

    private List<string> ReadLines()
    {
        if (!File.Exists(filePath)) return new List<string>();
        return File.ReadAllLines(filePath, Encoding.UTF8).ToList();
    }

    /// <summary>
    /// Locate the [mcp_servers.&lt;name&gt;] block. Returns (start, end) inclusive
    /// of the lines to remove, or (-1, -1) if not present.
    /// </summary>
    private static (int start, int end) FindSection(List<string> lines, string sectionHeader)
    {
        var start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            // Use exact-line match (after trimming leading whitespace), not StartsWith,
            // so [mcp_servers.concord-mcp] doesn't match the primary header
            // (and also so the primary header doesn't match anything else).
            if (lines[i].TrimStart() == sectionHeader)
            {
                start = i;
                break;
            }
        }
        if (start < 0) return (-1, -1);

        var end = lines.Count - 1;
        for (int i = start + 1; i < lines.Count; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith("[", StringComparison.Ordinal))
            {
                end = i - 1;
                break;
            }
        }
        return (start, end);
    }

    private void WriteAtomic(List<string> lines)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
        // Journaled-rename swap (see McpJsonConfigurator.WriteAtomic for the
        // same pattern and rationale).
        if (File.Exists(filePath))
            File.Replace(tmp, filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else
            File.Move(tmp, filePath);
    }
}
