namespace Concord.Core.Tests;

using Xunit;
using Terminal;

public class HostContextTests
{
    [Fact]
    public void TargetMode_DefaultsToUninitialized_WhenHostHasNotSetIt()
    {
        HostContext.Reset();
        Assert.Equal(TargetMode.Uninitialized, HostContext.TargetMode);
    }

    [Fact]
    public void TargetMode_ReturnsValueSetByHost()
    {
        HostContext.Reset();
        HostContext.Initialize(TargetMode.Studio11x);
        Assert.Equal(TargetMode.Studio11x, HostContext.TargetMode);
    }

    [Fact]
    public void Initialize_Throws_WhenCalledTwice()
    {
        HostContext.Reset();
        HostContext.Initialize(TargetMode.Studio11x);
        Assert.Throws<InvalidOperationException>(() => HostContext.Initialize(TargetMode.Studio10x));
    }

    [Fact]
    public void Initialize_Throws_WhenCalledWithUninitialized()
    {
        HostContext.Reset();
        Assert.Throws<ArgumentException>(() => HostContext.Initialize(TargetMode.Uninitialized));
    }
}
