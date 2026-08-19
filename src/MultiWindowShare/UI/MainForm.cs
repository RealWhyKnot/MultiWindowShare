using System.Windows.Forms;
using MultiWindowShare.App;
using MultiWindowShare.Audio;
using MultiWindowShare.Capture;

namespace MultiWindowShare.UI;

internal sealed class MainForm : Form
{
    private readonly WindowPicker _picker = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _sink = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _start = new() { Text = "Start", Width = 90 };
    private readonly AppSettings _settings = AppSettings.Load();
    private CompositorForm? _output;

    public MainForm()
    {
        Text = "MultiWindowShare";
        Icon = AppIcon.Value;
        ClientSize = new Size(560, 460);
        StartPosition = FormStartPosition.CenterScreen;
        RestoreGeometry();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        layout.Controls.Add(new Label { Text = "Windows to share (toggle any time, even mid-share)", Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(_picker, 0, 1);
        layout.Controls.Add(new Label { Text = "Audio sink (a virtual cable keeps the mix silent locally)", Dock = DockStyle.Fill }, 0, 2);
        layout.Controls.Add(_sink, 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(_start);
        layout.Controls.Add(buttons, 0, 4);

        Controls.Add(layout);

        _start.Click += (_, _) => ToggleShare();
        _picker.TargetChecked += OnTargetChecked;
        _picker.TargetUnchecked += hwnd => _output?.RemoveSource(hwnd);

        LoadAudioSinks();
    }

    private void LoadAudioSinks()
    {
        int selected = 0;
        IReadOnlyList<AudioEndpoint> endpoints = AudioEndpoints.Render();
        for (int i = 0; i < endpoints.Count; i++)
        {
            _sink.Items.Add(endpoints[i]);
            if (endpoints[i].Id == _settings.SinkDeviceId)
            {
                selected = i;
            }
        }

        if (_sink.Items.Count > 0)
        {
            _sink.SelectedIndex = selected;
        }
    }

    private void ToggleShare()
    {
        if (_output is not null)
        {
            _output.Close();
            return;
        }

        IReadOnlyList<CaptureTarget> targets = _picker.CheckedTargets;
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Pick at least one window.", "MultiWindowShare");
            return;
        }

        if (_sink.SelectedItem is AudioEndpoint endpoint)
        {
            _settings.SinkDeviceId = endpoint.Id;
            _settings.Save();
        }

        var output = new CompositorForm(targets, _settings.CanvasWidth, _settings.CanvasHeight);
        output.SourceClosed += hwnd => _picker.SetChecked(hwnd, false);
        output.FormClosed += (_, _) =>
        {
            _output = null;
            _start.Text = "Start";
        };
        _output = output;
        _start.Text = "Stop";
        output.Show(this);
    }

    private void OnTargetChecked(CaptureTarget target)
    {
        if (_output is null)
        {
            return;
        }

        try
        {
            _output.AddSource(target);
        }
        catch (Exception ex)
        {
            _picker.SetChecked(target.Handle, false);
            MessageBox.Show(this, $"Could not capture {target.Title}: {ex.Message}", "MultiWindowShare");
        }
    }

    private void RestoreGeometry()
    {
        if (_settings.MainWindowWidth <= 0)
        {
            return;
        }

        var bounds = new Rectangle(_settings.MainWindowX, _settings.MainWindowY, _settings.MainWindowWidth, _settings.MainWindowHeight);
        if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.MainWindowX = bounds.X;
        _settings.MainWindowY = bounds.Y;
        _settings.MainWindowWidth = bounds.Width;
        _settings.MainWindowHeight = bounds.Height;
        _settings.Save();
        base.OnFormClosing(e);
    }
}
