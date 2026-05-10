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

// v4.2.1 introspection result types.

/// <summary>
/// "Is Maia generating?" snapshot. Reason discriminates so callers can tune
/// thresholds independently — e.g. ignore <c>recent-dom-mutation</c> with
/// idle_for_ms over a softer threshold while still respecting a visible spinner.
/// </summary>
public sealed record BusyResult(
    bool Busy,
    string Reason,
    long IdleForMs,
    string? Spinner = null);

/// <summary>
/// Result of <c>maia__new_chat</c>. Ok=false when the new-chat button could
/// not be located (DOM rename — heuristic needs refresh) or the click threw.
/// </summary>
public sealed record NewChatResult(
    bool Ok,
    DateTimeOffset? StartedAt = null,
    string? Error = null);

/// <summary>
/// Per-transport entry in <see cref="RouterHealthSnapshot.Transports"/>.
/// </summary>
public sealed record TransportHealth(
    string Name,
    int Tier,
    bool Available,
    double LastLatencyMs,
    string? Reason);

/// <summary>
/// Bridge state without traffic — the answer to "is the bridge alive AND
/// is Maia alive?" decomposed enough that the caller can decide what to do
/// next without running another roundtrip.
/// </summary>
public sealed record RouterHealthSnapshot(
    DateTimeOffset? LastProbeAt,
    IReadOnlyList<TransportHealth> Transports,
    int ActiveBindings,
    string? ForcedTier);
