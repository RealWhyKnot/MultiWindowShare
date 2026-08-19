using MultiWindowShare.Core;
using Xunit;

namespace MultiWindowShare.Tests;

public class VirtualSinkTests
{
    [Theory]
    [InlineData("CABLE Input (VB-Audio Virtual Cable)")]
    [InlineData("VoiceMeeter Input (VB-Audio VoiceMeeter VAIO)")]
    [InlineData("Line 1 (Virtual Audio Cable)")]
    [InlineData("MWS SILENT SINK - DO NOT ROUTE (VB-Audio Virtual Cable B)")]
    public void RecognisesVirtualSinks(string friendlyName)
    {
        Assert.True(VirtualSink.IsLikelyVirtualSink(friendlyName));
    }

    [Theory]
    [InlineData("Speakers (Realtek(R) Audio)")]
    [InlineData("Headphones (2- HyperX Cloud II)")]
    [InlineData("LG HDR 4K (NVIDIA High Definition Audio)")]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsRealPlaybackDevices(string friendlyName)
    {
        Assert.False(VirtualSink.IsLikelyVirtualSink(friendlyName));
    }
}
