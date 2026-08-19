namespace MultiWindowShare.Core;

// Decides which enumerated top-level windows are worth offering in the picker. Helper windows that
// exist only to carry a message loop -- 1x1 frame-rate pumps, floating palettes, the desktop shell
// -- are visible and titled like real windows, so they survive enumeration and waste a grid tile.
public static class WindowFilter
{
    // Smallest client edge worth a tile. Real app windows start an order of magnitude above this.
    public const int MinimumClientEdge = 32;

    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;

    public static bool IsShareable(int exStyle, bool isShellWindow, bool isMinimized, int clientWidth, int clientHeight)
    {
        if (isShellWindow)
        {
            return false;
        }

        // WS_EX_APPWINDOW forces a taskbar button back onto a tool window, so it marks the rare
        // tool-styled window that is still a real app window.
        if ((exStyle & WsExToolWindow) != 0 && (exStyle & WsExAppWindow) == 0)
        {
            return false;
        }

        // A minimized window collapses its client rect to near nothing, so the size floor cannot be
        // applied to one without hiding the windows the picker exists to prompt a restore for.
        return isMinimized || (clientWidth >= MinimumClientEdge && clientHeight >= MinimumClientEdge);
    }
}
