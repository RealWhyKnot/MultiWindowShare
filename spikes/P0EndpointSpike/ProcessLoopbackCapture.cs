using System.Runtime.InteropServices;
using System.Threading;

namespace P0EndpointSpike;

// Hand-rolled WASAPI process-loopback capture. NAudio doesn't expose the ActivateAudioInterfaceAsync
// + AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK path, which captures the render audio of a target
// PID and its process tree regardless of which endpoint that process renders to. Polling (not
// event-driven) keeps the interop small; this is a spike, not the production capture path.
internal sealed class ProcessLoopbackCapture : IDisposable
{
    private const string VirtualLoopbackDevice = "VAD\\Process_Loopback";
    private const int ActivationTypeProcessLoopback = 1;
    private const int LoopbackModeIncludeTree = 0;
    private const int ShareModeShared = 0;
    private const uint StreamFlagsLoopback = 0x00020000;
    private const uint BufferFlagsSilent = 0x2;
    private const int SampleRate = 48000;
    private const int Channels = 2;

    private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

    private readonly int _pid;
    private readonly object _accLock = new();
    private IAudioClient? _client;
    private IAudioCaptureClient? _capture;
    private Thread? _thread;
    private volatile bool _running;
    private double _sumSq;
    private long _samples;

    public ProcessLoopbackCapture(int pid) => _pid = pid;

    public void Start()
    {
        _client = Activate(_pid);

        var format = new WaveFormatEx
        {
            wFormatTag = 1, // WAVE_FORMAT_PCM
            nChannels = Channels,
            nSamplesPerSec = SampleRate,
            wBitsPerSample = 16,
            nBlockAlign = Channels * 16 / 8,
            nAvgBytesPerSec = (uint)(SampleRate * Channels * 16 / 8),
            cbSize = 0,
        };

        const long hnsBuffer = 5_000_000; // 500 ms
        int hr = _client.Initialize(ShareModeShared, StreamFlagsLoopback, hnsBuffer, 0, ref format, IntPtr.Zero);
        Check(hr, "IAudioClient.Initialize");

        Check(_client.GetService(IID_IAudioCaptureClient, out object svc), "GetService(IAudioCaptureClient)");
        _capture = (IAudioCaptureClient)svc;

        Check(_client.Start(), "IAudioClient.Start");

        _running = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "loopback-capture" };
        _thread.Start();
    }

    // RMS (0..1) over the samples seen since the previous call, then resets the accumulator.
    public double ReadRmsAndReset()
    {
        lock (_accLock)
        {
            double rms = _samples > 0 ? Math.Sqrt(_sumSq / _samples) : 0.0;
            _sumSq = 0;
            _samples = 0;
            return rms;
        }
    }

    private IAudioClient Activate(int pid)
    {
        var activation = new AudioClientActivationParams
        {
            ActivationType = ActivationTypeProcessLoopback,
            TargetProcessId = pid,
            ProcessLoopbackMode = LoopbackModeIncludeTree,
        };

        int blobSize = Marshal.SizeOf<AudioClientActivationParams>();
        IntPtr blob = Marshal.AllocHGlobal(blobSize);
        IntPtr propvariant = Marshal.AllocHGlobal(24); // PROPVARIANT is 24 bytes on x64
        try
        {
            Marshal.StructureToPtr(activation, blob, false);

            // PROPVARIANT: WORD vt = VT_BLOB (65); then BLOB { ULONG cbSize @8; BYTE* pBlobData @16 }.
            for (int i = 0; i < 24; i++)
            {
                Marshal.WriteByte(propvariant, i, 0);
            }

            Marshal.WriteInt16(propvariant, 0, 65);
            Marshal.WriteInt32(propvariant, 8, blobSize);
            Marshal.WriteIntPtr(propvariant, 16, blob);

            var handler = new ActivationHandler();
            ActivateAudioInterfaceAsync(VirtualLoopbackDevice, IID_IAudioClient, propvariant, handler, out _);

            if (!handler.Done.WaitOne(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("ActivateAudioInterfaceAsync did not complete");
            }

            Check(handler.ActivateResult, "process-loopback activation");
            if (handler.Interface is not IAudioClient client)
            {
                throw new InvalidOperationException("activation returned no IAudioClient");
            }

            return client;
        }
        finally
        {
            Marshal.FreeHGlobal(propvariant);
            Marshal.FreeHGlobal(blob);
        }
    }

    private unsafe void CaptureLoop()
    {
        IAudioCaptureClient capture = _capture!;
        while (_running)
        {
            capture.GetNextPacketSize(out uint packet);
            while (packet > 0)
            {
                if (capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _) != 0)
                {
                    break;
                }

                long n = (long)frames * Channels;
                double sq = 0;
                if ((flags & BufferFlagsSilent) == 0 && data != IntPtr.Zero)
                {
                    short* p = (short*)data;
                    for (long k = 0; k < n; k++)
                    {
                        double v = p[k] / 32768.0;
                        sq += v * v;
                    }
                }

                lock (_accLock)
                {
                    _sumSq += sq;
                    _samples += n;
                }

                capture.ReleaseBuffer(frames);
                capture.GetNextPacketSize(out packet);
            }

            Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join(500);
        if (_client != null)
        {
            _client.Stop();
            Marshal.ReleaseComObject(_client);
            _client = null;
        }

        if (_capture != null)
        {
            Marshal.ReleaseComObject(_capture);
            _capture = null;
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr != 0)
        {
            throw new COMException($"{what} failed (0x{hr:X8})", hr);
        }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation operation);

    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler
    {
        public readonly ManualResetEvent Done = new(false);
        public int ActivateResult = -1;
        public object? Interface;

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            operation.GetActivateResult(out ActivateResult, out Interface);
            Done.Set();
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientActivationParams
{
    public int ActivationType;
    public int TargetProcessId;
    public int ProcessLoopbackMode;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WaveFormatEx
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

[ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
}

[ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    [PreserveSig]
    int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activateInterface);
}

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig]
    int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, ref WaveFormatEx format, IntPtr sessionGuid);

    [PreserveSig]
    int GetBufferSize(out uint numBufferFrames);

    [PreserveSig]
    int GetStreamLatency(out long latency);

    [PreserveSig]
    int GetCurrentPadding(out uint numPaddingFrames);

    [PreserveSig]
    int IsFormatSupported(int shareMode, ref WaveFormatEx format, IntPtr closestMatch);

    [PreserveSig]
    int GetMixFormat(out IntPtr deviceFormat);

    [PreserveSig]
    int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int SetEventHandle(IntPtr eventHandle);

    [PreserveSig]
    int GetService([MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig]
    int GetBuffer(out IntPtr data, out uint numFramesToRead, out uint flags, out ulong devicePosition, out ulong qpcPosition);

    [PreserveSig]
    int ReleaseBuffer(uint numFramesRead);

    [PreserveSig]
    int GetNextPacketSize(out uint numFramesInNextPacket);
}
