using MultiWindowShare.Core;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.DirectX.Direct3D11;

namespace MultiWindowShare.Capture;

internal readonly record struct SourceStatus(string Title, int Width, int Height, bool HasFrame, int FramesArrived, string? LastError);

// Draws every captured window into one swap chain as a grid. Each tile is a full-viewport triangle
// sampling that window's texture, so scaling is free and no vertex buffer is needed. Sources can be
// added and removed while rendering runs, and the swap chain follows the output window's size.
internal sealed class GridCompositor : IDisposable
{
    private const string Hlsl = """
        struct VSOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        VSOut VS(uint id : SV_VertexID)
        {
            VSOut o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.uv = uv;
            o.pos = float4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, 0.0, 1.0);
            return o;
        }

        Texture2D srcTexture : register(t0);
        SamplerState srcSampler : register(s0);

        float4 PS(VSOut i) : SV_TARGET
        {
            return srcTexture.Sample(srcSampler, i.uv);
        }
        """;

    private const int SwapChainBufferCount = 2;
    // Present(1) ties the frame rate to the monitor refresh; it doubles as the frame limiter.
    private const uint PresentSyncInterval = 1;
    private static readonly Color4 Background = new(0f, 0f, 0f, 1f);

    private readonly object _contextLock = new();
    private readonly List<WindowCapture> _sources = [];
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDirect3DDevice _winRtDevice;
    private readonly IDXGISwapChain1 _swapChain;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11SamplerState _sampler;

    private ID3D11RenderTargetView _renderTarget;
    private long _pendingSize;
    private SourceSize[] _lastSizes = [];
    private int _lastCanvasWidth;
    private int _lastCanvasHeight;
    private IReadOnlyList<Tile> _tiles = [];

    public GridCompositor(IntPtr hwnd, int width, int height)
    {
        Width = width;
        Height = height;

        D3D11.D3D11CreateDevice(
            null!,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            null!,
            out ID3D11Device? device,
            out ID3D11DeviceContext? context).CheckError();

        _device = device!;
        _context = context!;
        _winRtDevice = Direct3D11Interop.CreateWinRtDevice(_device);

        using IDXGIFactory2 factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        var description = new SwapChainDescription1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = SwapChainBufferCount,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1, 0),
            AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
            Scaling = Scaling.Stretch,
        };

        _swapChain = factory.CreateSwapChainForHwnd(_device, hwnd, description);
        using ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTarget = _device.CreateRenderTargetView(backBuffer);

        ReadOnlyMemory<byte> vertexCode = Compiler.Compile(Hlsl, "VS", "grid.hlsl", "vs_5_0");
        ReadOnlyMemory<byte> pixelCode = Compiler.Compile(Hlsl, "PS", "grid.hlsl", "ps_5_0");
        _vertexShader = _device.CreateVertexShader(vertexCode.Span);
        _pixelShader = _device.CreatePixelShader(pixelCode.Span);

        _sampler = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunc = ComparisonFunction.Never,
            MaxLOD = float.MaxValue,
        });
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    // Raised on a WinRT thread after a source whose window closed has been removed.
    public event Action<IntPtr>? SourceClosed;

    public void AddSource(IntPtr hwnd, string title)
    {
        var capture = new WindowCapture(hwnd, title, _device, _context, _contextLock, _winRtDevice, OnSourceClosed);
        lock (_contextLock)
        {
            _sources.Add(capture);
        }
    }

    public void RemoveSource(IntPtr hwnd)
    {
        lock (_contextLock)
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                if (_sources[i].Hwnd == hwnd)
                {
                    WindowCapture capture = _sources[i];
                    _sources.RemoveAt(i);
                    capture.Dispose();
                    return;
                }
            }
        }
    }

    private void OnSourceClosed(WindowCapture capture)
    {
        RemoveSource(capture.Hwnd);
        SourceClosed?.Invoke(capture.Hwnd);
    }

    // Safe from any thread; the resize itself happens on the render thread so it can never race
    // Present. Rapid size changes coalesce to the newest one.
    public void QueueResize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Interlocked.Exchange(ref _pendingSize, ((long)width << 32) | (uint)height);
    }

    public IReadOnlyList<SourceStatus> SourceStatuses()
    {
        lock (_contextLock)
        {
            var statuses = new SourceStatus[_sources.Count];
            for (int i = 0; i < _sources.Count; i++)
            {
                WindowCapture s = _sources[i];
                statuses[i] = new SourceStatus(s.Title, s.Width, s.Height, s.View is not null, s.FramesArrived, s.LastError);
            }

            return statuses;
        }
    }

    public void Render()
    {
        lock (_contextLock)
        {
            ApplyPendingResize();

            _context.OMSetRenderTargets(_renderTarget);
            _context.ClearRenderTargetView(_renderTarget, Background);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(_pixelShader);
            _context.PSSetSampler(0, _sampler);

            IReadOnlyList<Tile> tiles = CurrentTiles();
            for (int i = 0; i < _sources.Count; i++)
            {
                WindowCapture source = _sources[i];
                if (source.View is null)
                {
                    continue;
                }

                Tile tile = tiles[i];
                _context.RSSetViewport(tile.X, tile.Y, tile.Width, tile.Height);
                _context.PSSetShaderResource(0, source.View);
                _context.Draw(3, 0);
            }
        }

        _swapChain.Present(PresentSyncInterval, PresentFlags.None);
    }

    private void ApplyPendingResize()
    {
        long pending = Interlocked.Exchange(ref _pendingSize, 0);
        if (pending == 0)
        {
            return;
        }

        int width = (int)(pending >> 32);
        int height = (int)pending;
        if (width == Width && height == Height)
        {
            return;
        }

        // Flip-model ResizeBuffers refuses while any backbuffer reference is alive.
        _context.UnsetRenderTargets();
        _renderTarget.Dispose();
        _swapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None).CheckError();
        using ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTarget = _device.CreateRenderTargetView(backBuffer);
        Width = width;
        Height = height;
    }

    // Layout is pure and deterministic, so it only needs recomputing when a source size or the
    // canvas actually changed; steady state reuses the cached tiles.
    private IReadOnlyList<Tile> CurrentTiles()
    {
        bool changed = _sources.Count != _lastSizes.Length || Width != _lastCanvasWidth || Height != _lastCanvasHeight;
        for (int i = 0; !changed && i < _sources.Count; i++)
        {
            changed = _lastSizes[i].Width != _sources[i].Width || _lastSizes[i].Height != _sources[i].Height;
        }

        if (changed)
        {
            var sizes = new SourceSize[_sources.Count];
            for (int i = 0; i < _sources.Count; i++)
            {
                sizes[i] = new SourceSize(_sources[i].Width, _sources[i].Height);
            }

            _tiles = GridLayout.Compute(sizes, Width, Height);
            _lastSizes = sizes;
            _lastCanvasWidth = Width;
            _lastCanvasHeight = Height;
        }

        return _tiles;
    }

    public void Dispose()
    {
        lock (_contextLock)
        {
            foreach (WindowCapture source in _sources)
            {
                source.Dispose();
            }

            _sources.Clear();
        }

        _sampler.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
        _renderTarget.Dispose();
        _swapChain.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
