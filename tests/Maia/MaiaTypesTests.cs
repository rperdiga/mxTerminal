using FluentAssertions;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class MaiaTypesTests
{
    [Fact]
    public void HealthStatus_Available_DefaultsTo_ReasonNull()
    {
        var h = new HealthStatus(Available: true, Tier: 1, Name: "cdp_injected", LatencyMs: 12.5);
        h.Available.Should().BeTrue();
        h.Reason.Should().BeNull();
    }

    [Fact]
    public void TransportUnavailable_CarriesReason()
    {
        var ex = new TransportUnavailable("Maia panel not visible.");
        ex.Message.Should().Be("Maia panel not visible.");
    }
}
