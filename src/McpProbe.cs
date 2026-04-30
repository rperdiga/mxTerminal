using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MxStudioProTerminal;

/// <summary>
/// Sends an MCP <c>initialize</c> JSON-RPC request to the configured port and
/// reports whether a real MCP server answered. Used before writing any CLI
/// config so we don't point users at a dead URL.
/// </summary>
public static class McpProbe
{
    public sealed record Result(bool Ok, string Message, string? ServerName = null);

    public static async Task<Result> ProbeAsync(int port, CancellationToken ct = default)
    {
        var url = $"http://localhost:{port}/mcp";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var body = """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{
                  "protocolVersion":"2025-03-26","capabilities":{},
                  "clientInfo":{"name":"mx-terminal-probe","version":"1.0"}
                }}
                """;
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            content.Headers.Add("Accept", "application/json, text/event-stream");

            using var resp = await http.PostAsync(url, content, ct);
            if (!resp.IsSuccessStatusCode)
                return new Result(false, $"HTTP {(int)resp.StatusCode} from {url}");

            var text = await resp.Content.ReadAsStringAsync(ct);
            // Streamable HTTP servers may return SSE; pull out the data: line if so.
            var json = ExtractJson(text);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("serverInfo", out var info) &&
                info.TryGetProperty("name", out var name))
            {
                return new Result(true, $"Connected to {name.GetString()}", name.GetString());
            }
            return new Result(false, $"Endpoint at {url} did not return an MCP initialize response");
        }
        catch (HttpRequestException ex)
        {
            return new Result(false, $"Could not connect to {url}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new Result(false, $"Connection to {url} timed out");
        }
        catch (Exception ex)
        {
            return new Result(false, $"Probe failed: {ex.Message}");
        }
    }

    private static string ExtractJson(string body)
    {
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith("{")) return trimmed;
        // SSE: lines like  "data: {...}\n\n"
        foreach (var line in body.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (l.StartsWith("data:"))
                return l[5..].TrimStart();
        }
        return body;
    }
}
