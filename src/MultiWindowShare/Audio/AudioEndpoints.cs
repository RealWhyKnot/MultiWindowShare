using MultiWindowShare.Core;
using NAudio.CoreAudioApi;

namespace MultiWindowShare.Audio;

public sealed record AudioEndpoint(string Id, string Name, bool IsVirtual)
{
    public override string ToString() => IsVirtual ? $"{Name}  (virtual)" : Name;
}

public static class AudioEndpoints
{
    // Active playback devices, virtual sinks first: those are the ones that make a source inaudible
    // locally while still carrying full-volume audio for capture.
    public static IReadOnlyList<AudioEndpoint> Render()
    {
        using var enumerator = new MMDeviceEnumerator();
        var endpoints = new List<AudioEndpoint>();
        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                endpoints.Add(new AudioEndpoint(device.ID, device.FriendlyName, VirtualSink.IsLikelyVirtualSink(device.FriendlyName)));
            }
        }

        return [.. endpoints.OrderByDescending(e => e.IsVirtual).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}
