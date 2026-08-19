using MultiWindowShare.Core;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.DirectX.Direct3D11;

namespace MultiWindowShare.Capture;

// Draws every captured window into one swap chain as a grid. Each tile is a full-viewport triangle
// sampling that window's texture, so scaling is free and no vertex buffer is needed.
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

    private readonly object _contextLock = new();
    private readonly List<WindowCapture> _sources = [];
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDirect3DDevice _winRtDevice;
    private readonly IDXGISwapChain1 _swapChain;
    private readonly ID3D11RenderTargetView _renderTarget;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11SamplerState _sampler;

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
            BufferCount = 2,
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

    public int Width { get; }

    public int Height { get; }

    public void AddSource(IntPtr hwnd, string title)
    {
        var capture = new WindowCapture(hwnd, title, _device, _context, _contextLock, _winRtDevice);
        lock (_contextLock)
        {
            _sources.Add(capture);
        }
    }

    // Prints each source's captured resolution and returns how many have produced a frame.
    public int ReportSourceSizes()
    {
        lock (_contextLock)
        {
            int live = 0;
            foreach (WindowCapture source in _sources)
            {
                bool has = source.View is not null;
                string state = has ? $"{source.Width}x{source.Height}" : "no texture";
                Console.WriteLine($"  {state}  arrived={source.FramesArrived}  {source.Title}");
                if (source.LastError is not null)
                {
                    Console.WriteLine($"        error: {source.LastError}");
                }

                if (has)
                {
                    live++;
                }
            }

            return live;
        }
    }

    public void Render()
    {
        lock (_contextLock)
        {
            _context.OMSetRenderTargets(_renderTarget);
            _context.ClearRenderTargetView(_renderTarget, new Color4(0f, 0f, 0f, 1f));
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(_pixelShader);
            _context.PSSetSampler(0, _sampler);

            IReadOnlyList<Tile> tiles = GridLayout.Compute(_sources.Count, Width, Height);
            for (int i = 0; i < _sources.Count; i++)
            {
                WindowCapture source = _sources[i];
                if (source.View is null)
                {
                    continue;
                }

                Tile tile = GridLayout.Fit(tiles[i], source.Width, source.Height);
                _context.RSSetViewport(tile.X, tile.Y, tile.Width, tile.Height);
                _context.PSSetShaderResource(0, source.View);
                _context.Draw(3, 0);
            }
        }

        _swapChain.Present(1, PresentFlags.None);
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
