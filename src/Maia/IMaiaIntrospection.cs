namespace Terminal.Maia;

/// <summary>
/// Optional companion to <see cref="IMaiaTransport"/>: read-only DOM
/// introspection (<see cref="BusyAsync"/>) and the one DOM-mutation
/// command we expose (<see cref="NewChatAsync"/>). Only transports that
/// can drive the WebView's DOM implement this — currently
/// <see cref="CdpInjectedTransport"/>. The router exposes the first
/// active introspection-capable transport via <see cref="MaiaRouter.GetIntrospection"/>;
/// <see cref="MaiaActions"/> consults it for <c>maia__busy</c> and
/// <c>maia__new_chat</c>.
/// <para>
/// Kept separate from <see cref="IMaiaTransport"/> so test stubs of the
/// conversational interface don't have to fake DOM-level operations they
/// don't care about.
/// </para>
/// </summary>
public interface IMaiaIntrospection
{
    Task<BusyResult> BusyAsync(CancellationToken ct);
    Task<NewChatResult> NewChatAsync(CancellationToken ct);
    /// <summary>
    /// Trigger a one-shot completion scan inside the JS bridge. Used by the
    /// CDP heartbeat in <see cref="CdpClient"/> to defeat WebView2 background-
    /// tab throttling: when Maia is hidden behind another pane,
    /// <c>setInterval(scanForCompletions, 500)</c> can drift to seconds. The
    /// heartbeat's persistent WebSocket is unaffected by tab visibility, so
    /// driving the scan from C# keeps detection latency bounded.
    /// </summary>
    Task ScanAsync(CancellationToken ct);
}
