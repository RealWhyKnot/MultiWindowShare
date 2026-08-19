using System.Windows.Forms;
using MultiWindowShare.Capture;
using MultiWindowShare.UI;

namespace MultiWindowShare;

// Headless check that the capture and compositing path really runs: builds the D3D11 device, compiles
// the shaders, captures real windows and presents frames, then reports the measured sizes. Run with
// --smoke. The window sits off-screen so the check does not interrupt whatever is on the desktop.
internal static class Smoke
{
    public static int Run(int sourceCount, int frames)
    {
        List<CaptureTarget> targets = [.. WindowEnumerator.TopLevelWindows(Environment.ProcessId).Take(sourceCount)];
        if (targets.Count == 0)
        {
            Console.WriteLine("no capturable windows found");
            return 1;
        }

        using var form = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(-4000, -4000),
            ClientSize = new System.Drawing.Size(640, 360),
            ShowInTaskbar = false,
            Text = "MultiWindowShare smoke",
        };
        form.Show();

        using var compositor = new GridCompositor(form.Handle, 1920, 1080);
        foreach (CaptureTarget target in targets)
        {
            compositor.AddSource(target.Handle, target.Title);
            Console.WriteLine($"source: {target.Title}");
        }

        int presented = 0;
        for (int i = 0; i < frames; i++)
        {
            compositor.Render();
            presented++;
            Application.DoEvents();
        }

        Console.WriteLine($"presented {presented} frames at 1920x1080");
        int live = compositor.ReportSourceSizes();
        Console.WriteLine(live == targets.Count
            ? $"all {live} sources delivered frames"
            : $"only {live} of {targets.Count} sources delivered frames");

        form.Close();

        using (var output = new CompositorForm(targets, 1920, 1080))
        {
            output.StartPosition = FormStartPosition.Manual;
            output.Location = new System.Drawing.Point(-4000, -4000);
            output.ShowInTaskbar = false;
            output.Show();
            for (int i = 0; i < 60; i++)
            {
                Application.DoEvents();
                Thread.Sleep(16);
            }

            output.Close();
        }

        Console.WriteLine("output window open/close ok");
        return live > 0 ? 0 : 1;
    }
}
