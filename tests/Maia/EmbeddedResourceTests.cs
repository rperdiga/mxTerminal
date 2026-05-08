using FluentAssertions;
using Xunit;

namespace Terminal.Tests.Maia;

public class EmbeddedResourceTests
{
    [Fact]
    public void MaiaAgentJs_IsEmbedded()
    {
        using var s = typeof(Terminal.Maia.CdpClient).Assembly
            .GetManifestResourceStream("Terminal.Maia.maia_agent.js");
        s.Should().NotBeNull("the JS agent must be embedded for CdpInjectedTransport to inject it");
        using var r = new StreamReader(s!);
        var text = r.ReadToEnd();
        text.Should().Contain("window.__maiaBridge");
        text.Should().Contain("MX_CHAT_INPUT");
    }
}
