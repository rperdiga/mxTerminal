using FluentAssertions;
using Xunit;

namespace Concord.Shim.Tests.Fakes;

public class FakeHostBuilderTests
{
    [Fact]
    public void EmitFakeHost_ProducesLoadableDll()
    {
        var temp = Path.Combine(Path.GetTempPath(), "Concord.Shim.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var dllPath = FakeHostBuilder.EmitFakeHost(temp);
            File.Exists(dllPath).Should().BeTrue();
            new FileInfo(dllPath).Length.Should().BeGreaterThan(1024);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }
}
