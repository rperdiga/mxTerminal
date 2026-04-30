using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class RingBufferTests
{
    [Fact]
    public void NewBuffer_IsEmpty()
    {
        var rb = new RingBuffer(capacity: 16);
        rb.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void Write_BelowCapacity_ReturnsAllBytes()
    {
        var rb = new RingBuffer(capacity: 16);
        rb.Write(new byte[] { 1, 2, 3 });
        rb.Snapshot().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Write_ExactCapacity_ReturnsAllBytes()
    {
        var rb = new RingBuffer(capacity: 4);
        rb.Write(new byte[] { 1, 2, 3, 4 });
        rb.Snapshot().Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void Write_OverCapacity_DropsOldest()
    {
        var rb = new RingBuffer(capacity: 4);
        rb.Write(new byte[] { 1, 2, 3, 4, 5, 6 });
        rb.Snapshot().Should().Equal(3, 4, 5, 6);
    }

    [Fact]
    public void Write_MultipleAppends_ReturnsInOrder()
    {
        var rb = new RingBuffer(capacity: 8);
        rb.Write(new byte[] { 1, 2, 3 });
        rb.Write(new byte[] { 4, 5 });
        rb.Snapshot().Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Write_ChunkLargerThanCapacity_KeepsLastCapacityBytes()
    {
        var rb = new RingBuffer(capacity: 3);
        rb.Write(new byte[] { 1, 2, 3, 4, 5 });
        rb.Snapshot().Should().Equal(3, 4, 5);
    }

    [Fact]
    public void Write_WrapAround_RebuildsCorrectOrder()
    {
        var rb = new RingBuffer(capacity: 4);
        rb.Write(new byte[] { 1, 2, 3 });
        rb.Write(new byte[] { 4, 5, 6 });   // wraps: writeIndex was 3, now overwrites slot 0 with 5, slot 1 with 6
        rb.Snapshot().Should().Equal(3, 4, 5, 6);
    }

    [Fact]
    public void Constructor_InvalidCapacity_Throws()
    {
        Action act = () => new RingBuffer(capacity: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
