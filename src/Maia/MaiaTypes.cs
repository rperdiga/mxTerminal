using System.Text.Json.Nodes;

namespace Terminal.Maia;

public sealed record HealthStatus(
    bool Available,
    int Tier,
    string Name,
    double LatencyMs,
    string? Reason = null);

public sealed record SendResult(
    string Handle,
    string Sentinel,
    string TransportUsed,
    DateTimeOffset SentAt);

public sealed record StatusResult(
    bool Done,
    string Response,
    bool Streaming,
    double ElapsedSec,
    string TransportUsed,
    // v4.2.0: when true, the JS-side ticket was lost (WebView reload between
    // Send and Status); the router's handleToTransport binding survived. The
    // router translates this into a "lost" MCP discriminator so callers can
    // re-ask instead of looping.
    bool Lost = false,
    // v4.2.0: when true, the underlying transport reports the JS-side never
    // had this handle. The router decides whether to surface as "lost" (we
    // had bound it; ticket vanished) or "unknown" (typo / never sent).
    bool UnknownHandle = false);

public class TransportError : Exception
{
    public TransportError(string message) : base(message) { }
    public TransportError(string message, Exception inner) : base(message, inner) { }
}

public sealed class TransportUnavailable : TransportError
{
    public TransportUnavailable(string reason) : base(reason) { }
    public TransportUnavailable(string reason, Exception inner) : base(reason, inner) { }

    /// <summary>
    /// True when the failure represents a transport-layer disconnect (WebSocket
    /// dropped, IOException on send/receive, remote close). The reconnect path
    /// in <c>CdpClient.EvaluateAsync</c> uses this flag to decide whether to
    /// attempt a one-shot reconnect+retry. Set explicitly at the catch sites
    /// in <c>CdpClient</c>; do NOT rely on string-matching the message.
    /// </summary>
    public bool IsDisconnect { get; init; }
}

public sealed class CdpProtocolException : TransportError
{
    public string CdpMethod { get; }
    public JsonNode? CdpResponse { get; }
    public CdpProtocolException(string method, JsonNode? response, string message)
        : base(message)
    {
        CdpMethod = method;
        CdpResponse = response;
    }
}
