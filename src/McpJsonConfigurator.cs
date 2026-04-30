using System.Text.Json;
using System.Text.Json.Nodes;

namespace Terminal;

/// <summary>
/// Manages the project-level <c>.mcp.json</c> file (read by Claude Code and
/// GitHub Copilot CLI). Upserts/removes named server entries while preserving
/// anything else the user has in the file.
/// </summary>
public sealed class McpJsonConfigurator
{
    public const string ServerName = "mendix-studio-pro";
    public const string ActionsServerName = "mendix-studio-pro-actions";

    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    private readonly string filePath;

    public McpJsonConfigurator(string projectDir)
    {
        filePath = Path.Combine(projectDir, ".mcp.json");
    }

    public void Upsert(string url)        => UpsertNamed(ServerName, url);
    public void Remove()                  => RemoveNamed(ServerName);
    public void UpsertActions(string url) => UpsertNamed(ActionsServerName, url);
    public void RemoveActions()           => RemoveNamed(ActionsServerName);

    private void UpsertNamed(string serverName, string url)
    {
        var root = ReadOrEmpty();
        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }
        servers[serverName] = new JsonObject
        {
            ["type"] = "http",
            ["url"]  = url,
        };
        WriteAtomic(root);
    }

    private void RemoveNamed(string serverName)
    {
        if (!File.Exists(filePath)) return;
        var root = ReadOrEmpty();
        if (root["mcpServers"] is JsonObject servers && servers.ContainsKey(serverName))
        {
            servers.Remove(serverName);
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
