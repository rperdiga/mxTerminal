namespace Terminal.Maia;

/// <summary>
/// One way of talking to Maia. The router probes all registered transports,
/// picks the lowest-tier (highest-priority) available, and demotes on per-call
/// TransportUnavailable. Future: tier-0 NativeMcpTransport when Mendix ships
/// 11.12 native MCP-server-as-tool.
/// </summary>
public interface IMaiaTransport
{
    string Name { get; }
    int Tier { get; }
    Task<HealthStatus> HealthCheckAsync(CancellationToken ct);
    Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct);
    Task<StatusResult> StatusAsync(string handle, CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
}
