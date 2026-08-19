using MultiWindowShare.Core;
using Xunit;

namespace MultiWindowShare.Tests;

public class RingBufferTests
{
    [Fact]
    public void RoundTripsWhatFits()
    {
        var ring = new RingBuffer(8);
        ring.Write([1f, 2f, 3f]);
        Assert.Equal(3, ring.Count);

        var dest = new float[3];
        Assert.Equal(3, ring.Read(dest));
        Assert.Equal([1f, 2f, 3f], dest);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void DropsTheOldestOnOverflow()
    {
        var ring = new RingBuffer(4);
        ring.Write([1f, 2f, 3f, 4f, 5f, 6f]);
        Assert.Equal(4, ring.Count);

        var dest = new float[4];
        ring.Read(dest);
        Assert.Equal([3f, 4f, 5f, 6f], dest);
    }

    [Fact]
    public void ReadReturnsOnlyWhatItHas()
    {
        var ring = new RingBuffer(8);
        ring.Write([7f, 8f]);

        var dest = new float[5];
        Assert.Equal(2, ring.Read(dest));
        Assert.Equal(7f, dest[0]);
        Assert.Equal(8f, dest[1]);
    }

    [Fact]
    public void RejectsANonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer(0));
    }
}
