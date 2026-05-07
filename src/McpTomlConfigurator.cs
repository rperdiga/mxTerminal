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
    public const string ActionsServerName = "mendix-studio-pro-actions";

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

    public void Upsert(string url)        => UpsertNamed(ServerName, url);
    public void Remove()                  => RemoveNamed(ServerName);
    public void UpsertActions(string url) => UpsertNamed(ActionsServerName, url);
    public void RemoveActions()           => RemoveNamed(ActionsServerName);

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
        var header = HeaderFor(serverName);
        var lines = ReadLines();
        var (start, end) = FindSection(lines, header);
        if (start < 0) return;

        var until = end;
        if (until + 1 < lines.Count && lines[until + 1].Trim().Length == 0) until++;
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
    /// </summary>
    private static (int start, int end) FindSection(List<string> lines, string sectionHeader)
    {
        var start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            // Use exact-line match (after trimming leading whitespace), not StartsWith,
            // so [mcp_servers.mendix-studio-pro-actions] doesn't match the primary header.
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
        if (File.Exists(filePath)) File.Delete(filePath);
        File.Move(tmp, filePath);
    }
}
