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
    string TransportUsed);

public class TransportError : Exception
{
    public TransportError(string message) : base(message) { }
    public TransportError(string message, Exception inner) : base(message, inner) { }
}

public sealed class TransportUnavailable : TransportError
{
    public TransportUnavailable(string reason) : base(reason) { }
    public TransportUnavailable(string reason, Exception inner) : base(reason, inner) { }
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
