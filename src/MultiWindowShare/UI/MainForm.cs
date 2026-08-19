using System.Windows.Forms;
using MultiWindowShare.Audio;
using MultiWindowShare.Settings;

namespace MultiWindowShare.UI;

internal sealed class MainForm : Form
{
    private readonly CheckedListBox _windows = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private readonly ComboBox _sink = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _refresh = new() { Text = "Refresh", Width = 90 };
    private readonly Button _start = new() { Text = "Start", Width = 90 };
    private readonly AppSettings _settings = AppSettings.Load();

    public MainForm()
    {
        Text = "MultiWindowShare";
        Icon = AppIcon.Value;
        ClientSize = new System.Drawing.Size(560, 460);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        layout.Controls.Add(new Label { Text = "Windows to share", Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(_windows, 0, 1);
        layout.Controls.Add(new Label { Text = "Audio sink (a virtual cable keeps the mix silent locally)", Dock = DockStyle.Fill }, 0, 2);
        layout.Controls.Add(_sink, 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(_start);
        buttons.Controls.Add(_refresh);
        layout.Controls.Add(buttons, 0, 4);

        Controls.Add(layout);

        _refresh.Click += (_, _) => LoadLists();
        _start.Click += (_, _) => Start();

        LoadLists();
    }

    private void LoadLists()
    {
        _windows.Items.Clear();
        foreach (CaptureTarget target in WindowEnumerator.TopLevelWindows(Environment.ProcessId))
        {
            _windows.Items.Add(target);
        }

        _sink.Items.Clear();
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

    private void Start()
    {
        var targets = _windows.CheckedItems.Cast<CaptureTarget>().ToList();
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

        new CompositorForm(targets, _settings.CanvasWidth, _settings.CanvasHeight).Show(this);
    }
}
