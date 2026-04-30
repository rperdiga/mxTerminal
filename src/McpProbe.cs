using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Terminal;

/// <summary>
/// Sends an MCP <c>initialize</c> JSON-RPC request to the configured port and
/// reports whether a real MCP server answered. Used before writing any CLI
/// config so we don't point users at a dead URL.
/// </summary>
public static class McpProbe
{
    public sealed record Result(bool Ok, string Message, string? ServerName = null);

    // Reuse one client — repeated `new HttpClient` inside short-lived scopes
    // can exhaust ephemeral ports under load.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static async Task<Result> ProbeAsync(int port, Logger? log = null, CancellationToken ct = default)
    {
        var url = $"http://localhost:{port}/mcp";
        try
        {
            const string body = """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{
                  "protocolVersion":"2025-03-26","capabilities":{},
                  "clientInfo":{"name":"mx-terminal-probe","version":"1.0"}
                }}
                """;

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            // Accept goes on the REQUEST, not the content. Streamable-HTTP MCP
            // servers respond either with application/json or text/event-stream.
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var resp = await Http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                log?.Warn($"MCP probe got HTTP {(int)resp.StatusCode} from {url}; body: {Truncate(text)}");
                return new Result(false, $"HTTP {(int)resp.StatusCode} from {url}");
            }

            var json = ExtractJson(text);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("serverInfo", out var info) &&
                info.TryGetProperty("name", out var name))
            {
                log?.Info($"MCP probe OK on {url}: {name.GetString()}");
                return new Result(true, $"Connected to {name.GetString()}", name.GetString());
            }
            log?.Warn($"MCP probe got unexpected payload from {url}: {Truncate(text)}");
            return new Result(false, $"Endpoint at {url} did not return an MCP initialize response");
        }
        catch (HttpRequestException ex)
        {
            log?.Warn($"MCP probe HttpRequestException for {url}: {ex.Message}");
            return new Result(false, $"Could not connect to {url}: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            log?.Warn($"MCP probe timeout for {url}: {ex.Message}");
            return new Result(false, $"Connection to {url} timed out");
        }
        catch (Exception ex)
        {
            log?.Error($"MCP probe unexpected failure for {url}", ex);
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

    private static string Truncate(string s, int max = 300) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";
}
