using System.Text;

namespace Terminal;

/// <summary>
/// Manages the <c>[mcp_servers.mendix-studio-pro]</c> section of the user-level
/// Codex config at <c>~/.codex/config.toml</c>. Codex's MCP support is stdio-only
/// so we wire it through the npx <c>mcp-remote</c> bridge.
///
/// Hand-rolled TOML editing — no need for a full parser since we own a single
/// well-known section and never touch anything else.
/// </summary>
public sealed class McpTomlConfigurator
{
    public const string ServerName = "mendix-studio-pro";
    private static readonly string SectionHeader = $"[mcp_servers.{ServerName}]";

    private readonly string filePath;

    public McpTomlConfigurator()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        filePath = Path.Combine(home, ".codex", "config.toml");
    }

    public string FilePath => filePath;

    public void Upsert(string url)
    {
        var lines = ReadLines();
        var (start, end) = FindSection(lines);
        var newSection = new[]
        {
            SectionHeader,
            "command = \"npx\"",
            $"args = [\"-y\", \"mcp-remote\", \"{url}\"]",
        };

        if (start < 0)
        {
            // No existing section — append at end (with a separating blank line).
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.AddRange(newSection);
        }
        else
        {
            // Replace [start..end] inclusive with the new section.
            lines.RemoveRange(start, end - start + 1);
            lines.InsertRange(start, newSection);
        }
        WriteAtomic(lines);
    }

    public void Remove()
    {
        if (!File.Exists(filePath)) return;
        var lines = ReadLines();
        var (start, end) = FindSection(lines);
        if (start < 0) return;

        // Also drop one trailing blank line if it follows the section, to avoid pile-up.
        var until = end;
        if (until + 1 < lines.Count && lines[until + 1].Trim().Length == 0) until++;
        // …and one preceding blank line if it directly preceded the section.
        var from = start;
        if (from > 0 && lines[from - 1].Trim().Length == 0) from--;

        lines.RemoveRange(from, until - from + 1);
        WriteAtomic(lines);
    }

    private List<string> ReadLines()
    {
        if (!File.Exists(filePath)) return new List<string>();
        return File.ReadAllLines(filePath, Encoding.UTF8).ToList();
    }

    /// <summary>
    /// Locate the [mcp_servers.&lt;name&gt;] block. Returns (start, end) inclusive
    /// of the lines to remove, or (-1, -1) if not present.
    /// The block runs from the header line up to (but not including) the next
    /// top-level `[` line, or end-of-file.
    /// </summary>
    private static (int start, int end) FindSection(List<string> lines)
    {
        var start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith(SectionHeader, StringComparison.Ordinal))
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

        // Trim trailing blank lines.
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
        if (File.Exists(filePath)) File.Delete(filePath);
        File.Move(tmp, filePath);
    }
}
