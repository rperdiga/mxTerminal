using FluentAssertions;
using Xunit;

namespace Concord.Shim.Tests;

public class RuntimeHostLocatorTests
{
    [Theory]
    [InlineData("10.24.13", "bin-10x")]
    [InlineData("10.21.1", "bin-10x")]
    [InlineData("10.0.0", "bin-10x")]
    [InlineData("11.0.0", "bin-11x")]
    [InlineData("11.6.2", "bin-11x")]
    [InlineData("11.10.0", "bin-11x")]
    [InlineData("11.9.5", "bin-11x")]
    [InlineData("12.0.0-preview", "bin-11x")] // forward-active branch wins; SP12 may invalidate
    public void BinFolderName_ForVersion_ReturnsExpected(string version, string expected)
    {
        RuntimeHostLocator.BinFolderName(version).Should().Be(expected);
    }

    [Fact]
    public void BinFolderName_ForUnknownInput_DefaultsTo11x()
    {
        RuntimeHostLocator.BinFolderName(null).Should().Be("bin-11x");
        RuntimeHostLocator.BinFolderName("").Should().Be("bin-11x");
        RuntimeHostLocator.BinFolderName("garbage").Should().Be("bin-11x");
    }

    [Fact]
    public void ResolveBinDirectory_AnchorsToAssemblyLocation_NotAppDomainBase()
    {
        // The locator must resolve relative to the shim's own assembly path,
        // not AppDomain.BaseDirectory — Phase 0 found the latter returns
        // Studio Pro's install dir under the cache-snapshot deployment model.
        var anchorAssemblyDir = Path.GetDirectoryName(typeof(RuntimeHostLocator).Assembly.Location)!;
        var got = RuntimeHostLocator.ResolveBinDirectoryFromAnchor(anchorAssemblyDir, "bin-11x");
        got.Should().Be(Path.Combine(anchorAssemblyDir, "bin-11x"));
    }
}
