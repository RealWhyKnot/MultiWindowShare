using System.Runtime.InteropServices;
using System.Text;

namespace MultiWindowShare;

public readonly record struct CaptureTarget(IntPtr Handle, string Title)
{
    public override string ToString() => Title;
}

// Lists top-level windows that Windows.Graphics.Capture can actually target: visible, titled, and
// not cloaked (suspended UWP apps and windows on another virtual desktop are cloaked and have no
// live surface). Minimized windows are intentionally kept -- they can't be captured while minimized,
// but the picker should still show them so the user knows to restore them.
public static class WindowEnumerator
{
    private const int DwmwaCloaked = 14;

    // excludeProcessId drops our own windows, which would otherwise let the user capture the
    // compositor output and mirror it into itself.
    public static IReadOnlyList<CaptureTarget> TopLevelWindows(int excludeProcessId = 0)
    {
        var results = new List<CaptureTarget>();
        EnumWindows((hwnd, _) =>
        {
            if (IsCapturable(hwnd, excludeProcessId, out string title))
            {
                results.Add(new CaptureTarget(hwnd, title));
            }

            return true;
        }, IntPtr.Zero);
        return results;
    }

    private static bool IsCapturable(IntPtr hwnd, int excludeProcessId, out string title)
    {
        title = string.Empty;
        if (!IsWindowVisible(hwnd))
        {
            return false;
        }

        if (excludeProcessId != 0)
        {
            GetWindowThreadProcessId(hwnd, out int pid);
            if (pid == excludeProcessId)
            {
                return false;
            }
        }

        if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        int length = GetWindowTextLength(hwnd);
        if (length == 0)
        {
            return false;
        }

        var buffer = new StringBuilder(length + 1);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        title = buffer.ToString();
        return title.Length > 0;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lparam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lparam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
