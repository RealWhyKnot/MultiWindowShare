using Vortice.Direct3D11;
using Vortice.Mathematics;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace MultiWindowShare.Capture;

// One captured window. Frames arrive on a pool thread, so each frame is copied into a texture this
// class owns and the compositor samples that copy on its own thread. Both sides take the shared
// device-context lock, since an ID3D11DeviceContext cannot be used concurrently.
internal sealed class WindowCapture : IDisposable
{
    private const DirectXPixelFormat PoolFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    private const int PoolBufferCount = 2;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly object _contextLock;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly IDirect3DDevice _winRtDevice;
    private readonly Action<WindowCapture>? _onClosed;

    private ID3D11Texture2D? _latest;
    private ID3D11ShaderResourceView? _view;
    private bool _disposed;
    private int _framesArrived;
    private string? _lastError;

    public WindowCapture(
        IntPtr hwnd,
        string title,
        ID3D11Device device,
        ID3D11DeviceContext context,
        object contextLock,
        IDirect3DDevice winRtDevice,
        Action<WindowCapture>? onClosed = null)
    {
        Hwnd = hwnd;
        Title = title;
        _device = device;
        _context = context;
        _contextLock = contextLock;
        _winRtDevice = winRtDevice;
        _onClosed = onClosed;

        _item = CaptureInterop.CreateForWindow(hwnd);
        _item.Closed += OnItemClosed;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            winRtDevice,
            PoolFormat,
            PoolBufferCount,
            _item.Size);
        _pool.FrameArrived += OnFrameArrived;

        _session = _pool.CreateCaptureSession(_item);
        TrySetBorderless(_session);
        _session.StartCapture();
    }

    public IntPtr Hwnd { get; }

    public string Title { get; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public ID3D11ShaderResourceView? View => _view;

    public int FramesArrived => _framesArrived;

    public string? LastError => _lastError;

    // WinRT swallows exceptions thrown out of a free-threaded event handler, so failures here are
    // recorded rather than raised; without this a broken frame path looks identical to a window
    // that simply never repaints.
    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            ProcessFrame(sender);
        }
        catch (Exception e)
        {
            _lastError = $"{e.GetType().Name}: {e.Message}";
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        _onClosed?.Invoke(this);
    }

    private void ProcessFrame(Direct3D11CaptureFramePool sender)
    {
        using Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        Interlocked.Increment(ref _framesArrived);
        SizeInt32 size = frame.ContentSize;
        lock (_contextLock)
        {
            if (_disposed)
            {
                return;
            }

            using ID3D11Texture2D source = Direct3D11Interop.GetTexture(frame.Surface);
            Texture2DDescription desc = source.Description;

            // The pool surface keeps its old dimensions for a frame after the window resizes, so
            // only the content region is copied; the padding is stale pixels at the wrong aspect.
            int width = Math.Clamp(size.Width, 1, (int)desc.Width);
            int height = Math.Clamp(size.Height, 1, (int)desc.Height);

            if (_latest is null || Width != width || Height != height)
            {
                _view?.Dispose();
                _latest?.Dispose();

                desc.Width = (uint)width;
                desc.Height = (uint)height;
                desc.BindFlags = BindFlags.ShaderResource;
                desc.Usage = ResourceUsage.Default;
                desc.CPUAccessFlags = CpuAccessFlags.None;
                desc.MiscFlags = ResourceOptionFlags.None;

                _latest = _device.CreateTexture2D(desc);
                _view = _device.CreateShaderResourceView(_latest);
                Width = width;
                Height = height;
            }

            _context.CopySubresourceRegion(_latest, 0, 0, 0, 0, source, 0, new Box(0, 0, 0, width, height, 1));
        }

        // A resized window keeps producing frames at the old pool size until the pool is told.
        if (size.Width > 0 && size.Height > 0 && (size.Width != _item.Size.Width || size.Height != _item.Size.Height))
        {
            _pool.Recreate(_winRtDevice, PoolFormat, PoolBufferCount, size);
        }
    }

    // Borderless capture needs Windows 11 plus a declared capability; an unpackaged app is expected
    // to be refused, which only means the yellow border stays.
    private static void TrySetBorderless(GraphicsCaptureSession session)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        try
        {
            session.IsBorderRequired = false;
        }
        catch (Exception)
        {
            // Left bordered.
        }
    }

    public void Dispose()
    {
        lock (_contextLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _item.Closed -= OnItemClosed;
            _pool.FrameArrived -= OnFrameArrived;
            _session.Dispose();
            _pool.Dispose();
            _view?.Dispose();
            _latest?.Dispose();
            _view = null;
            _latest = null;
        }
    }
}
