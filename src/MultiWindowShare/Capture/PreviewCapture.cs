using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MultiWindowShare.Capture;

// GDI snapshots for picker thumbnails. PrintWindow paints synchronously with the target window, so
// callers must stay off the UI thread and skip hung windows.
internal static class PreviewCapture
{
    private const uint PwRenderFullContent = 2;
    private const uint WmGetIcon = 0x7F;
    private const nint IconSmall2 = 2;
    private const int GclpHicon = -14;
    private const uint SmtoAbortIfHung = 2;

    // Paints hwnd into the reusable scratch bitmap, then scales the result into a fresh thumbnail,
    // aspect preserved. Returns null when the window is minimized, hung, or refuses to paint.
    public static Bitmap? TryCapture(IntPtr hwnd, ref Bitmap? scratch, int thumbWidth, int thumbHeight)
    {
        if (IsIconic(hwnd) || IsHungAppWindow(hwnd) || !GetWindowRect(hwnd, out Rect rect))
        {
            return null;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        if (scratch is null || scratch.Width < width || scratch.Height < height)
        {
            int scratchWidth = Math.Max(width, scratch?.Width ?? 0);
            int scratchHeight = Math.Max(height, scratch?.Height ?? 0);
            scratch?.Dispose();
            scratch = new Bitmap(scratchWidth, scratchHeight);
        }

        using (var g = Graphics.FromImage(scratch))
        {
            IntPtr hdc = g.GetHdc();
            bool painted;
            try
            {
                painted = PrintWindow(hwnd, hdc, PwRenderFullContent);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }

            if (!painted)
            {
                return null;
            }
        }

        var thumb = new Bitmap(thumbWidth, thumbHeight);
        using (var g = Graphics.FromImage(thumb))
        {
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            double scale = Math.Min(thumbWidth / (double)width, thumbHeight / (double)height);
            int w = Math.Max(1, (int)(width * scale));
            int h = Math.Max(1, (int)(height * scale));
            g.DrawImage(scratch,
                new Rectangle((thumbWidth - w) / 2, (thumbHeight - h) / 2, w, h),
                new Rectangle(0, 0, width, height),
                GraphicsUnit.Pixel);
        }

        return thumb;
    }

    // Fallback thumbnail for windows without a paintable surface: the app icon on a dark ground.
    public static Bitmap? IconThumb(IntPtr hwnd, int thumbWidth, int thumbHeight)
    {
        IntPtr hicon = IntPtr.Zero;
        if (SendMessageTimeout(hwnd, WmGetIcon, IconSmall2, IntPtr.Zero, SmtoAbortIfHung, 100, out nint result) != IntPtr.Zero)
        {
            hicon = result;
        }

        if (hicon == IntPtr.Zero)
        {
            hicon = GetClassLongPtrW(hwnd, GclpHicon);
        }

        if (hicon == IntPtr.Zero)
        {
            return null;
        }

        var thumb = new Bitmap(thumbWidth, thumbHeight);
        using var g = Graphics.FromImage(thumb);
        g.Clear(Color.FromArgb(32, 32, 32));
        // Icon.FromHandle does not own the shared handle, so disposing the wrapper is safe.
        using var icon = Icon.FromHandle(hicon);
        g.DrawIcon(icon, new Rectangle((thumbWidth - 32) / 2, (thumbHeight - 32) / 2, 32, 32));
        return thumb;
    }

    public static bool IsWindowAlive(IntPtr hwnd) => IsWindow(hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, nint wparam, IntPtr lparam, uint flags, uint timeoutMs, out nint result);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClassLongPtrW(IntPtr hwnd, int index);
}
