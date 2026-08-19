using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace MultiWindowShare.Capture;

// Windows.Graphics.Capture exposes no public constructor for a per-window capture item; you reach it
// through the GraphicsCaptureItem activation factory's IGraphicsCaptureItemInterop. This is the
// CsWinRT idiom for getting that factory interface and marshalling the returned ABI pointer back to
// a projected GraphicsCaptureItem.
internal static class CaptureInterop
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd) =>
        Create(interop => interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid));

    public static GraphicsCaptureItem CreateForMonitor(IntPtr hmonitor) =>
        Create(interop => interop.CreateForMonitor(hmonitor, GraphicsCaptureItemGuid));

    private static GraphicsCaptureItem Create(Func<IGraphicsCaptureItemInterop, IntPtr> createAbi)
    {
        IObjectReference factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        IntPtr itemPtr = createAbi(interop);
        try
        {
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            Marshal.Release(itemPtr);
        }
    }
}

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

    IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
}
