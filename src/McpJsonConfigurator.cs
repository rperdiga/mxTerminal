using System.Text.Json;
using System.Text.Json.Nodes;

namespace MxStudioProTerminal;

/// <summary>
/// Manages the project-level <c>.mcp.json</c> file (read by Claude Code and
/// GitHub Copilot CLI). Upserts/removes a single named server entry while
/// preserving anything else the user has in the file.
/// </summary>
public sealed class McpJsonConfigurator
{
    public const string ServerName = "mendix-studio-pro";

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
    };

    private readonly string filePath;

    public McpJsonConfigurator(string projectDir)
    {
        filePath = Path.Combine(projectDir, ".mcp.json");
    }

    /// <summary>Ensure the file contains our entry pointing at the given URL.</summary>
    public void Upsert(string url)
    {
        var root = ReadOrEmpty();
        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }
        servers[ServerName] = new JsonObject
        {
            ["type"] = "http",
            ["url"]  = url,
        };
        WriteAtomic(root);
    }

    /// <summary>Remove our entry. Leaves the rest of the file untouched.
    /// Deletes the file if it ends up empty.</summary>
    public void Remove()
    {
        if (!File.Exists(filePath)) return;
        var root = ReadOrEmpty();
        if (root["mcpServers"] is JsonObject servers && servers.ContainsKey(ServerName))
        {
            servers.Remove(ServerName);
            if (servers.Count == 0) root.Remove("mcpServers");
        }

        if (root.Count == 0)
        {
            try { File.Delete(filePath); } catch { /* best-effort */ }
            return;
        }
        WriteAtomic(root);
    }

    private JsonObject ReadOrEmpty()
    {
        if (!File.Exists(filePath)) return new JsonObject();
        try
        {
            using var stream = File.OpenRead(filePath);
            var node = JsonNode.Parse(stream);
            return node as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private void WriteAtomic(JsonObject root)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(WriteOpts) + Environment.NewLine);
        if (File.Exists(filePath)) File.Delete(filePath);
        File.Move(tmp, filePath);
    }
}
