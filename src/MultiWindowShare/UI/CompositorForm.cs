using System.Windows.Forms;
using MultiWindowShare.Capture;

namespace MultiWindowShare.UI;

// The window the user shares in Discord. Rendering runs on its own thread so a busy UI thread
// cannot stall the grid.
internal sealed class CompositorForm : Form
{
    private readonly IReadOnlyList<CaptureTarget> _targets;
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;
    private GridCompositor? _compositor;
    private Thread? _renderThread;
    private volatile bool _running;

    public CompositorForm(IReadOnlyList<CaptureTarget> targets, int canvasWidth, int canvasHeight)
    {
        _targets = targets;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;

        Text = "MultiWindowShare Output";
        ClientSize = new System.Drawing.Size(canvasWidth / 2, canvasHeight / 2);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = System.Drawing.Color.Black;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        try
        {
            _compositor = new GridCompositor(Handle, _canvasWidth, _canvasHeight);
            foreach (CaptureTarget target in _targets)
            {
                _compositor.AddSource(target.Handle, target.Title);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not start capture: {ex.Message}", "MultiWindowShare",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            BeginInvoke(Close);
            return;
        }

        _running = true;
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "compositor-render" };
        _renderThread.Start();
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
                if (++failures >= 50)
                {
                    ReportRenderFailure(ex);
                    return;
                }

                Thread.Sleep(100);
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
        if (_renderThread is null || _renderThread.Join(5000))
        {
            _compositor?.Dispose();
        }

        _compositor = null;
        base.OnFormClosing(e);
    }
}
