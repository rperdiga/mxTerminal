using System.Text.Json.Nodes;

namespace Terminal.Maia;

/// <summary>
/// Connects to Studio Pro's WebView2 Maia panel via Chrome DevTools Protocol
/// and runs JS evaluations inside it. Owns the WMI process scan, the /json
/// endpoint discovery, and the WebSocket plumbing. The single seam tests fake
/// to cover all transports.
/// </summary>
public interface ICdpClient : IAsyncDisposable
{
    Task ConnectMaiaAsync(CancellationToken ct);
    Task<JsonNode?> EvaluateAsync(string js, TimeSpan? timeout = null, CancellationToken ct = default);
}
