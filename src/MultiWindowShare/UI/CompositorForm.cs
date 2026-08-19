using System.Windows.Forms;
using MultiWindowShare.Capture;

namespace MultiWindowShare.UI;

// The window the user shares in Discord. Rendering runs on its own thread so a busy UI thread
// cannot stall the grid.
internal sealed class CompositorForm : Form
{
    private const int MaxConsecutiveRenderFailures = 50;
    private const int RenderRetryDelayMs = 100;
    private const int CloseJoinTimeoutMs = 5000;

    private readonly IReadOnlyList<CaptureTarget> _targets;
    private GridCompositor? _compositor;
    private Thread? _renderThread;
    private volatile bool _running;

    public CompositorForm(IReadOnlyList<CaptureTarget> targets, int canvasWidth, int canvasHeight)
    {
        _targets = targets;

        Text = "MultiWindowShare Output";
        Icon = AppIcon.Value;
        ClientSize = new System.Drawing.Size(canvasWidth / 2, canvasHeight / 2);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = System.Drawing.Color.Black;
    }

    // Raised on the UI thread after a shared window closed and its tile was dropped.
    public event Action<IntPtr>? SourceClosed;

    public void AddSource(CaptureTarget target)
    {
        _compositor?.AddSource(target.Handle, target.Title);
    }

    public void RemoveSource(IntPtr hwnd)
    {
        _compositor?.RemoveSource(hwnd);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        try
        {
            // The swap chain matches the window's on-screen size: screen sharing captures rendered
            // pixels, so a larger fixed backbuffer would only be scaled down by DWM anyway.
            _compositor = new GridCompositor(Handle, ClientSize.Width, ClientSize.Height);
            _compositor.SourceClosed += OnCompositorSourceClosed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not start capture: {ex.Message}", "MultiWindowShare",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            BeginInvoke(Close);
            return;
        }

        // A window that died since it was picked should not take the whole session down with it.
        List<string> failed = [];
        foreach (CaptureTarget target in _targets)
        {
            try
            {
                _compositor.AddSource(target.Handle, target.Title);
            }
            catch (Exception ex)
            {
                failed.Add($"{target.Title}: {ex.Message}");
                SourceClosed?.Invoke(target.Handle);
            }
        }

        if (failed.Count > 0)
        {
            MessageBox.Show(this, $"Could not capture:\n{string.Join('\n', failed)}", "MultiWindowShare",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _running = true;
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "compositor-render" };
        _renderThread.Start();
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (WindowState != FormWindowState.Minimized)
        {
            _compositor?.QueueResize(ClientSize.Width, ClientSize.Height);
        }
    }

    // Arrives on a WinRT thread; forwarded to the UI thread so MainForm can update the picker.
    private void OnCompositorSourceClosed(IntPtr hwnd)
    {
        try
        {
            BeginInvoke(() => SourceClosed?.Invoke(hwnd));
        }
        catch (InvalidOperationException)
        {
            // The form is closing; nobody needs the notification.
        }
    }

    private void RenderLoop()
    {
        int failures = 0;
        while (_running)
        {
            try
            {
                _compositor?.Render();
                failures = 0;
            }
            catch (Exception ex)
            {
                // Transient device errors recover; five seconds of them (e.g. device removed) will not.
                if (++failures >= MaxConsecutiveRenderFailures)
                {
                    ReportRenderFailure(ex);
                    return;
                }

                Thread.Sleep(RenderRetryDelayMs);
            }
        }
    }

    private void ReportRenderFailure(Exception ex)
    {
        try
        {
            BeginInvoke(() =>
            {
                MessageBox.Show(this, $"Rendering stopped: {ex.Message}", "MultiWindowShare",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _running = false;
        // If the render thread is wedged inside Present, leaking beats disposing under it.
        if (_renderThread is null || _renderThread.Join(CloseJoinTimeoutMs))
        {
            _compositor?.Dispose();
        }

        _compositor = null;
        base.OnFormClosing(e);
    }
}
