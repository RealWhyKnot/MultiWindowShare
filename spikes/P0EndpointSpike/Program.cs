using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace P0EndpointSpike;

internal static class Program
{
    private static int Main()
    {
        int pid = Environment.ProcessId;
        Console.WriteLine($"P0 endpoint spike -- pid {pid}");
        Console.WriteLine("Rendering a 440 Hz tone and capturing this process via WASAPI process-loopback.");
        Console.WriteLine("Toggling this process's own session mute every 3 s; measuring captured RMS.");
        Console.WriteLine();

        using var render = new WasapiOut(AudioClientShareMode.Shared, 100);
        render.Init(new SineProvider(440, 0.25f));
        render.Play();

        using var capture = new ProcessLoopbackCapture(pid);
        try
        {
            capture.Start();
        }
        catch (Exception e)
        {
            Console.WriteLine($"Process-loopback activation failed: {e.Message}");
            Console.WriteLine("Cannot run the no-install test on this machine.");
            return 2;
        }

        var mute = new SessionMute(pid);
        if (!mute.WaitForSession(TimeSpan.FromSeconds(3)))
        {
            Console.WriteLine("warning: could not find this process's audio session; mute toggling may be a no-op.");
            Console.WriteLine();
        }

        Thread.Sleep(500);
        capture.ReadRmsAndReset(); // discard warm-up

        double audibleSum = 0, mutedSum = 0;
        int audibleN = 0, mutedN = 0;
        bool muted = false;

        Console.WriteLine("  t(s)  state    capturedRMS");
        for (int step = 0; step < 24; step++)
        {
            if (step % 4 == 0)
            {
                muted = !muted;
                mute.Set(muted);
            }

            Thread.Sleep(750);
            double rms = capture.ReadRmsAndReset();
            Console.WriteLine($"  {step * 0.75,4:F1}  {(muted ? "MUTED  " : "audible")}  {rms:F5}");
            if (muted)
            {
                mutedSum += rms;
                mutedN++;
            }
            else
            {
                audibleSum += rms;
                audibleN++;
            }
        }

        mute.Set(false);
        render.Stop();

        double avgAudible = audibleN > 0 ? audibleSum / audibleN : 0;
        double avgMuted = mutedN > 0 ? mutedSum / mutedN : 0;
        Console.WriteLine();
        Console.WriteLine($"average captured RMS while audible: {avgAudible:F5}");
        Console.WriteLine($"average captured RMS while muted:   {avgMuted:F5}");
        Console.WriteLine();
        Verdict(avgAudible, avgMuted);
        return 0;
    }

    private static void Verdict(double audible, double muted)
    {
        if (audible < 1e-4)
        {
            Console.WriteLine("INCONCLUSIVE: no signal captured even while audible. Confirm the tone plays and the loopback targets this pid.");
            return;
        }

        double ratio = muted / audible;
        if (ratio > 0.5)
        {
            Console.WriteLine($"RESULT: capture SURVIVES session mute (muted/audible = {ratio:P0}). The no-install silence path is viable.");
        }
        else
        {
            Console.WriteLine($"RESULT: capture DIES on session mute (muted/audible = {ratio:P0}). The no-install path can't silence locally; fall back to VB-CABLE.");
        }

        Console.WriteLine();
        Console.WriteLine("Manual half (Discord): with a second account, share THIS window with sound while a row reads MUTED,");
        Console.WriteLine("and confirm the viewer hears the tone while your own speakers are silent.");
    }
}

internal sealed class SineProvider : ISampleProvider
{
    private readonly WaveFormat _format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private readonly double _step;
    private readonly float _amplitude;
    private double _phase;

    public SineProvider(double frequency, float amplitude)
    {
        _step = 2 * Math.PI * frequency / 48000;
        _amplitude = amplitude;
    }

    public WaveFormat WaveFormat => _format;

    public int Read(float[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i += 2)
        {
            float s = (float)(_amplitude * Math.Sin(_phase));
            buffer[offset + i] = s;
            buffer[offset + i + 1] = s;
            _phase += _step;
            if (_phase > 2 * Math.PI)
            {
                _phase -= 2 * Math.PI;
            }
        }

        return count;
    }
}

// Mutes/unmutes every render session belonging to a PID via ISimpleAudioVolume -- the exact control
// the production SilenceController will use. The spike measures whether process-loopback capture
// still hears the process after this mute is applied.
internal sealed class SessionMute
{
    private readonly int _pid;
    private readonly AudioSessionManager _manager;

    public SessionMute(int pid)
    {
        _pid = pid;
        MMDevice device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _manager = device.AudioSessionManager;
    }

    public bool WaitForSession(TimeSpan timeout)
    {
        DateTime end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            if (Find().Count > 0)
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    public void Set(bool mute)
    {
        foreach (AudioSessionControl session in Find())
        {
            session.SimpleAudioVolume.Mute = mute;
        }
    }

    private List<AudioSessionControl> Find()
    {
        var matches = new List<AudioSessionControl>();
        _manager.RefreshSessions();
        SessionCollection sessions = _manager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            AudioSessionControl session = sessions[i];
            if (session.GetProcessID == (uint)_pid)
            {
                matches.Add(session);
            }
        }

        return matches;
    }
}
