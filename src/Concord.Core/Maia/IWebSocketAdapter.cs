using System.Net.WebSockets;

namespace Terminal.Maia;

/// <summary>
/// Minimal adapter surface CdpClient calls on its underlying WebSocket.
/// Extracted so unit tests can inject a fake (drop-on-Nth-call,
/// timeout-on-receive, etc.) without standing up a real Studio Pro
/// CDP endpoint. Production uses <see cref="ClientWebSocketAdapter"/>.
/// <para>
/// The interface is intentionally narrow — only the methods/properties
/// CdpClient actually consumes. Adding members here means CdpClient
/// gained a new dependency on the underlying WebSocket; that's a real
/// observability boundary.
/// </para>
/// </summary>
public interface IWebSocketAdapter : IDisposable
{
    WebSocketState State { get; }
    Task ConnectAsync(Uri uri, CancellationToken ct);
    Task SendAsync(ArraySegment<byte> buf, WebSocketMessageType type, bool endOfMessage, CancellationToken ct);
    Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buf, CancellationToken ct);
    Task CloseAsync(WebSocketCloseStatus status, string? statusDescription, CancellationToken ct);
}

/// <summary>
/// Production adapter — thin pass-through to <see cref="ClientWebSocket"/>.
/// One instance per logical connection (matches ClientWebSocket's lifetime
/// model: dispose after close, allocate a fresh one for reconnect).
/// </summary>
public sealed class ClientWebSocketAdapter : IWebSocketAdapter
{
    private readonly ClientWebSocket inner = new();
    public WebSocketState State => inner.State;
    public Task ConnectAsync(Uri uri, CancellationToken ct) => inner.ConnectAsync(uri, ct);
    public Task SendAsync(ArraySegment<byte> buf, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
        => inner.SendAsync(buf, type, endOfMessage, ct);
    public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buf, CancellationToken ct)
        => inner.ReceiveAsync(buf, ct);
    public Task CloseAsync(WebSocketCloseStatus status, string? statusDescription, CancellationToken ct)
        => inner.CloseAsync(status, statusDescription, ct);
    public void Dispose() => inner.Dispose();
}
