namespace MultiWindowShare.Core;

// Recognises playback endpoints that have no speakers behind them. Routing a source there is what
// makes it inaudible locally while per-process loopback still captures it at full volume; muting
// cannot do the job, because the capture tap sits after the mute.
public static class VirtualSink
{
    private static readonly string[] Markers =
    [
        "vb-audio",
        "cable input",
        "voicemeeter",
        "virtual audio cable",
        "virtual cable",
        "vaio",
        "virtual audio device",
    ];

    public static bool IsLikelyVirtualSink(string friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return false;
        }

        string name = friendlyName.ToLowerInvariant();
        foreach (string marker in Markers)
        {
            if (name.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
