using MultiWindowShare.Core;
using Xunit;

namespace MultiWindowShare.Tests;

public class MixerTests
{
    [Fact]
    public void SumsTwoSources()
    {
        var dest = new float[3];
        float[] a = [0.1f, 0.2f, -0.3f];
        float[] b = [0.2f, -0.1f, 0.1f];

        Mixer.MixInto(dest, [a, b], [1f, 1f]);

        Assert.Equal(0.3f, dest[0], 1e-6f);
        Assert.Equal(0.1f, dest[1], 1e-6f);
        Assert.Equal(-0.2f, dest[2], 1e-6f);
    }

    [Fact]
    public void AppliesPerSourceGain()
    {
        var dest = new float[2];
        float[] a = [0.4f, 0.4f];

        Mixer.MixInto(dest, [a], [0.5f]);

        Assert.Equal(0.2f, dest[0], 1e-6f);
        Assert.Equal(0.2f, dest[1], 1e-6f);
    }

    [Fact]
    public void ClampsAnOverloadedSum()
    {
        var dest = new float[2];
        float[] a = [0.9f, -0.9f];
        float[] b = [0.9f, -0.9f];

        Mixer.MixInto(dest, [a, b], [1f, 1f]);

        Assert.Equal(1f, dest[0], 1e-6f);
        Assert.Equal(-1f, dest[1], 1e-6f);
    }

    [Fact]
    public void RejectsAGainCountMismatch()
    {
        var dest = new float[1];
        float[] a = [0f];

        Assert.Throws<ArgumentException>(() => Mixer.MixInto(dest, [a], [1f, 1f]));
    }
}
