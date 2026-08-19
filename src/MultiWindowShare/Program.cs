using System.Runtime.InteropServices;
using System.Windows.Forms;
using MultiWindowShare.Audio;
using MultiWindowShare.UI;

namespace MultiWindowShare;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [STAThread]
    private static void Main(string[] args)
    {
        // WinExe gets no console, so the diagnostic flags would print nowhere without this.
        if ((args.Contains("--list") || args.Contains("--devices") || args.Contains("--smoke"))
            && !Console.IsOutputRedirected)
        {
            AttachConsole(AttachParentProcess);
        }

        if (args.Contains("--list"))
        {
            foreach (CaptureTarget target in WindowEnumerator.TopLevelWindows(Environment.ProcessId))
            {
                Console.WriteLine($"0x{target.Handle.ToInt64():X}  {target.Title}");
            }

            return;
        }

        if (args.Contains("--devices"))
        {
            foreach (AudioEndpoint endpoint in AudioEndpoints.Render())
            {
                Console.WriteLine($"{(endpoint.IsVirtual ? "virtual" : "speaker")}  {endpoint.Name}");
            }

            return;
        }

        ApplicationConfiguration.Initialize();

        if (args.Contains("--smoke"))
        {
            Environment.ExitCode = Smoke.Run(sourceCount: 3, frames: 120);
            return;
        }

        Application.Run(new MainForm());
    }
}
