using GolfSimVision.Capture;
using Xunit;

namespace GolfSimVision.Tests.Capture;

public class RingBufferTests
{
    [Fact]
    public void AppendKeepsItemsInOrder()
    {
        var buffer = new RingBuffer<int>(capacity: 5);
        buffer.Append(1);
        buffer.Append(2);
        buffer.Append(3);

        Assert.Equal(new[] { 1, 2, 3 }, buffer.Snapshot());
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void DropsOldestOnceOverCapacity()
    {
        var buffer = new RingBuffer<int>(capacity: 3);
        for (var i = 1; i <= 5; i++) buffer.Append(i);

        Assert.Equal(new[] { 3, 4, 5 }, buffer.Snapshot());
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void LatestReturnsMostRecentlyAppended()
    {
        var buffer = new RingBuffer<string>(capacity: 2);
        Assert.Null(buffer.Latest());

        buffer.Append("a");
        buffer.Append("b");
        Assert.Equal("b", buffer.Latest());
    }

    [Fact]
    public void ClearEmptiesTheBuffer()
    {
        var buffer = new RingBuffer<int>(capacity: 5);
        buffer.Append(1);
        buffer.Append(2);

        buffer.Clear();

        Assert.Empty(buffer.Snapshot());
        Assert.Equal(0, buffer.Count);
    }
}
