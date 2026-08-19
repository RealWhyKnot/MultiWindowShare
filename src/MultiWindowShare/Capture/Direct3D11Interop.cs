using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace MultiWindowShare.Capture;

// Bridges Vortice's D3D11 objects and the WinRT surface types the capture API hands back.
internal static class Direct3D11Interop
{
    private static readonly Guid ID3D11Texture2DGuid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    public static IDirect3DDevice CreateWinRtDevice(ID3D11Device device)
    {
        using IDXGIDevice dxgi = device.QueryInterface<IDXGIDevice>();
        int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out IntPtr abi);
        if (hr != 0)
        {
            throw new COMException("CreateDirect3D11DeviceFromDXGIDevice failed", hr);
        }

        try
        {
            return MarshalInspectable<IDirect3DDevice>.FromAbi(abi);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    // The returned texture shares the frame's lifetime; copy out of it before releasing the frame.
    // The cast must go through CsWinRT's As<T>, which QueryInterfaces the underlying object: a plain
    // C# cast throws, because a projected WinRT object is not a classic RCW.
    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        IDirect3DDxgiInterfaceAccess access = surface.As<IDirect3DDxgiInterfaceAccess>();
        Guid iid = ID3D11Texture2DGuid;
        return new ID3D11Texture2D(access.GetInterface(ref iid));
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}

[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    IntPtr GetInterface([In] ref Guid iid);
}
