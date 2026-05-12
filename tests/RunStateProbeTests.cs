using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Terminal;
using Concord.Host11x.Interop;
using Xunit;

namespace Terminal.Tests;

public class RunStateProbeTests
{
    [Fact]
    public async Task IsRunningAsync_NoActiveConfiguration_ReturnsUnknown()
    {
        var probe = new RunStateProbe(getApplicationRootUrl: () => null);
        var result = await probe.IsRunningAsync();
        result.Should().Be(RunState.Unknown);
    }

    [Fact]
    public async Task IsRunningAsync_UrlEmpty_ReturnsUnknown()
    {
        var probe = new RunStateProbe(getApplicationRootUrl: () => "");
        var result = await probe.IsRunningAsync();
        result.Should().Be(RunState.Unknown);
    }

    [Fact]
    public async Task IsRunningAsync_UrlMalformed_ReturnsUnknown()
    {
        var probe = new RunStateProbe(getApplicationRootUrl: () => "not a url");
        var result = await probe.IsRunningAsync();
        result.Should().Be(RunState.Unknown);
    }

    [Fact]
    public async Task IsRunningAsync_PortOpen_ReturnsRunning()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var probe = new RunStateProbe(() => $"http://localhost:{port}");
            var result = await probe.IsRunningAsync();
            result.Should().Be(RunState.Running);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task IsRunningAsync_PortClosed_ReturnsStopped()
    {
        // Bind, capture port, immediately stop — port is now refused.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var probe = new RunStateProbe(() => $"http://localhost:{port}");
        var result = await probe.IsRunningAsync();
        result.Should().Be(RunState.Stopped);
    }

    [Fact]
    public void GetActivePort_ReadsPortFromUrl()
    {
        var probe = new RunStateProbe(() => "http://localhost:8123");
        probe.GetActivePort().Should().Be(8123);
    }

    [Fact]
    public void GetActiveUrl_Exposes_RawUrl()
    {
        var probe = new RunStateProbe(() => "http://localhost:8080");
        probe.GetActiveUrl().Should().Be("http://localhost:8080");
    }
}
