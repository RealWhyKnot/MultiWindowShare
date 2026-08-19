using MultiWindowShare.Core;
using Xunit;

namespace MultiWindowShare.Tests;

public class WindowFilterTests
{
    // Extended styles and client sizes below are the values these windows actually report.
    [Theory]
    [InlineData(0x00050100, 560, 460)] // MultiWindowShare itself (WinForms)
    [InlineData(0x00000100, 1278, 538)] // File Explorer
    [InlineData(0x00280100, 1280, 680)] // Chromium app window
    [InlineData(0x00200100, 1280, 680)] // Brave
    public void KeepsOrdinaryAppWindows(int exStyle, int width, int height)
    {
        Assert.True(WindowFilter.IsShareable(exStyle, isShellWindow: false, isMinimized: false, width, height));
    }

    [Fact]
    public void DropsPixelSizedHelperWindows()
    {
        // Parsec's ParsecMinFrameRate4 pump: topmost, transparent, layered, no tool-window bit.
        Assert.False(WindowFilter.IsShareable(0x08080028, isShellWindow: false, isMinimized: false, 1, 1));
    }

    [Fact]
    public void DropsTheShellWindow()
    {
        // Program Manager fills the desktop, so only the shell handle identifies it by size alone.
        Assert.False(WindowFilter.IsShareable(0x00200080, isShellWindow: true, isMinimized: false, 1280, 720));
    }

    [Fact]
    public void DropsToolWindows()
    {
        Assert.False(WindowFilter.IsShareable(0x00000080, isShellWindow: false, isMinimized: false, 800, 600));
    }

    [Fact]
    public void KeepsToolWindowsThatClaimATaskbarButton()
    {
        Assert.True(WindowFilter.IsShareable(0x000400C0, isShellWindow: false, isMinimized: false, 800, 600));
    }

    // Minimized windows must stay listed so the picker can prompt the user to restore them, and a
    // minimized client rect measures 0x0 (WinForms) or 144x20 (Chromium).
    [Theory]
    [InlineData(0, 0)]
    [InlineData(144, 20)]
    public void KeepsMinimizedWindowsDespiteTheirCollapsedClientRect(int width, int height)
    {
        Assert.True(WindowFilter.IsShareable(0x00050100, isShellWindow: false, isMinimized: true, width, height));
    }

    [Fact]
    public void DropsAMinimizedShellOrToolWindowAnyway()
    {
        Assert.False(WindowFilter.IsShareable(0x00000080, isShellWindow: false, isMinimized: true, 0, 0));
        Assert.False(WindowFilter.IsShareable(0x00000100, isShellWindow: true, isMinimized: true, 0, 0));
    }

    [Theory]
    [InlineData(WindowFilter.MinimumClientEdge, WindowFilter.MinimumClientEdge, true)]
    [InlineData(WindowFilter.MinimumClientEdge - 1, WindowFilter.MinimumClientEdge, false)]
    [InlineData(WindowFilter.MinimumClientEdge, WindowFilter.MinimumClientEdge - 1, false)]
    public void AppliesTheSizeFloorOnBothEdges(int width, int height, bool expected)
    {
        Assert.Equal(expected, WindowFilter.IsShareable(0x00000100, isShellWindow: false, isMinimized: false, width, height));
    }
}
