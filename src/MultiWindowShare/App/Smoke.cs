using System.Windows.Forms;
using MultiWindowShare.Capture;
using MultiWindowShare.UI;

namespace MultiWindowShare.App;

// Headless check that the capture and compositing path really runs: builds the D3D11 device, compiles
// the shaders, captures real windows, presents frames, then exercises runtime add/remove and swap
// chain resize. Run with --smoke. The window sits off-screen so the check does not interrupt whatever
// is on the desktop.
internal static class Smoke
{
    public static int Run(int sourceCount, int frames)
    {
        List<CaptureTarget> all = [.. WindowEnumerator.TopLevelWindows(Environment.ProcessId)];
        List<CaptureTarget> targets = [.. all.Take(sourceCount)];
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

        bool ok;
        using (var compositor = new GridCompositor(form.Handle, 1920, 1080))
        {
            foreach (CaptureTarget target in targets)
            {
                compositor.AddSource(target.Handle, target.Title);
                Console.WriteLine($"source: {target.Title}");
            }

            RenderFrames(compositor, frames);
            Console.WriteLine($"presented {frames} frames at {compositor.Width}x{compositor.Height}");

            int live = PrintStatuses(compositor);
            Console.WriteLine(live == targets.Count
                ? $"all {live} sources delivered frames"
                : $"only {live} of {targets.Count} sources delivered frames");
            ok = live > 0;

            ok &= CheckAddRemove(compositor, all.Skip(targets.Count).ToList(), targets.Count);
            ok &= CheckResize(compositor);
        }

        ok &= CheckPreviews(targets);

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
        return ok ? 0 : 1;
    }

    private static bool CheckAddRemove(GridCompositor compositor, List<CaptureTarget> spares, int baseline)
    {
        if (spares.Count == 0)
        {
            Console.WriteLine("runtime add/remove skipped: no spare window");
            return true;
        }

        CaptureTarget extra = spares[0];
        compositor.AddSource(extra.Handle, extra.Title);
        RenderFrames(compositor, 30);
        int afterAdd = compositor.SourceStatuses().Count;

        compositor.RemoveSource(extra.Handle);
        RenderFrames(compositor, 30);
        int afterRemove = compositor.SourceStatuses().Count;

        bool ok = afterAdd == baseline + 1 && afterRemove == baseline;
        Console.WriteLine(ok ? "runtime add/remove ok" : $"runtime add/remove failed: {afterAdd} then {afterRemove}");
        return ok;
    }

    // A single refusal is legitimate (a minimized window has nothing to paint), but a desktop where
    // no listed window produces a thumbnail means the preview path is broken.
    private static bool CheckPreviews(IReadOnlyList<CaptureTarget> targets)
    {
        Bitmap? scratch = null;
        int produced = 0;
        try
        {
            foreach (CaptureTarget target in targets)
            {
                using Bitmap? thumb = PreviewCapture.TryCapture(target.Handle, ref scratch, 96, 54);
                if (thumb is not null)
                {
                    produced++;
                }
            }
        }
        finally
        {
            scratch?.Dispose();
        }

        bool ok = produced > 0;
        Console.WriteLine(ok ? $"preview thumbs ok for {produced} of {targets.Count} windows" : "preview thumbs failed for every window");
        return ok;
    }

    private static bool CheckResize(GridCompositor compositor)
    {
        compositor.QueueResize(1280, 720);
        RenderFrames(compositor, 2);

        bool ok = compositor.Width == 1280 && compositor.Height == 720;
        Console.WriteLine(ok ? "swap chain resize ok" : $"swap chain resize failed: {compositor.Width}x{compositor.Height}");
        return ok;
    }

    private static void RenderFrames(GridCompositor compositor, int count)
    {
        for (int i = 0; i < count; i++)
        {
            compositor.Render();
            Application.DoEvents();
        }
    }

    private static int PrintStatuses(GridCompositor compositor)
    {
        int live = 0;
        foreach (SourceStatus status in compositor.SourceStatuses())
        {
            Console.WriteLine($"  {(status.HasFrame ? $"{status.Width}x{status.Height}" : "no texture")}  arrived={status.FramesArrived}  {status.Title}");
            if (status.LastError is not null)
            {
                Console.WriteLine($"        error: {status.LastError}");
            }

            if (status.HasFrame)
            {
                live++;
            }
        }

        return live;
    }
}
