using System.Net.Sockets;
using Terminal;
using Terminal.Interop;

namespace Concord.Host11x.Interop;

public sealed class RunStateProbe : IRunStateProbe
{
    private const int ConnectTimeoutMs = 250;

    private readonly Func<string?> getApplicationRootUrl;

    public RunStateProbe(Func<string?> getApplicationRootUrl)
    {
        this.getApplicationRootUrl = getApplicationRootUrl;
    }

    public string? GetActiveUrl() => getApplicationRootUrl();

    public int? GetActivePort()
    {
        var url = getApplicationRootUrl();
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return uri.Port;
    }

    public async Task<RunState> IsRunningAsync(CancellationToken ct = default)
    {
        var port = GetActivePort();
        if (port is null or <= 0) return RunState.Unknown;

        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectTimeoutMs);
            await client.ConnectAsync("127.0.0.1", port.Value, timeoutCts.Token);
            return RunState.Running;
        }
        catch (SocketException)
        {
            return RunState.Stopped;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Connect timed out — treat as stopped (caller can decide).
            return RunState.Stopped;
        }
        catch
        {
            return RunState.Unknown;
        }
    }
}
