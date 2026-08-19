using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using MultiWindowShare.Capture;

namespace MultiWindowShare.UI;

// Scrollable list of every shareable window with a periodically refreshed snapshot beside each
// title. A background thread does the enumeration and GDI capture; the UI thread only swaps
// bitmaps, so a slow target window can never stall the picker. Check state lives here, not in
// the native ListView checkboxes, so owner drawing and programmatic changes never fight the
// control's own toggle behaviour.
internal sealed class WindowPicker : ListView
{
    private const int RefreshIntervalMs = 1000;
    private const int ThumbWidth = 96;
    private const int ThumbHeight = 54;
    private const int RowHeight = 62;

    private readonly Dictionary<IntPtr, Bitmap> _thumbs = [];
    private readonly HashSet<IntPtr> _checked = [];
    private Thread? _worker;
    private volatile bool _running;

    public WindowPicker()
    {
        View = View.Details;
        OwnerDraw = true;
        FullRowSelect = true;
        MultiSelect = false;
        HeaderStyle = ColumnHeaderStyle.None;
        BorderStyle = BorderStyle.FixedSingle;
        DoubleBuffered = true;
        Columns.Add(string.Empty);
        SmallImageList = new ImageList { ImageSize = new Size(1, RowHeight) };

        DrawItem += OnDrawRow;
        MouseClick += OnRowClick;
        MouseDoubleClick += OnRowClick;
    }

    // Raised for user toggles only; programmatic SetChecked stays silent.
    public event Action<CaptureTarget>? TargetChecked;

    public event Action<IntPtr>? TargetUnchecked;

    public IReadOnlyList<CaptureTarget> CheckedTargets =>
        [.. Items.Cast<ListViewItem>().Select(i => (CaptureTarget)i.Tag!).Where(t => _checked.Contains(t.Handle))];

    public void SetChecked(IntPtr hwnd, bool value)
    {
        if (value ? _checked.Add(hwnd) : _checked.Remove(hwnd))
        {
            Invalidate();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FitColumn();
        if (_worker is null)
        {
            _running = true;
            _worker = new Thread(SweepLoop) { IsBackground = true, Name = "preview-capture" };
            _worker.Start();
        }
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        FitColumn();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space && SelectedItems.Count > 0)
        {
            ToggleRow(SelectedItems[0]);
            e.Handled = true;
        }
    }

    private void FitColumn()
    {
        if (Columns.Count > 0)
        {
            Columns[0].Width = Math.Max(0, ClientSize.Width - 4);
        }
    }

    private void OnRowClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && HitTest(e.Location).Item is { } item)
        {
            ToggleRow(item);
        }
    }

    private void ToggleRow(ListViewItem item)
    {
        var target = (CaptureTarget)item.Tag!;
        if (_checked.Add(target.Handle))
        {
            TargetChecked?.Invoke(target);
        }
        else
        {
            _checked.Remove(target.Handle);
            TargetUnchecked?.Invoke(target.Handle);
        }

        Invalidate(item.Bounds);
    }

    private void OnDrawRow(object? sender, DrawListViewItemEventArgs e)
    {
        var target = (CaptureTarget)e.Item.Tag!;
        Rectangle row = e.Bounds;

        e.Graphics.FillRectangle(e.Item.Selected ? SystemBrushes.Highlight : SystemBrushes.Window, row);

        CheckBoxRenderer.DrawCheckBox(
            e.Graphics,
            new Point(row.X + 8, row.Y + ((row.Height - 14) / 2)),
            _checked.Contains(target.Handle) ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal);

        var thumbBounds = new Rectangle(row.X + 32, row.Y + ((row.Height - ThumbHeight) / 2), ThumbWidth, ThumbHeight);
        if (_thumbs.TryGetValue(target.Handle, out Bitmap? thumb))
        {
            e.Graphics.DrawImage(thumb, thumbBounds);
        }
        else
        {
            e.Graphics.FillRectangle(SystemBrushes.ControlDark, thumbBounds);
        }

        var textBounds = new Rectangle(thumbBounds.Right + 8, row.Y, row.Right - thumbBounds.Right - 12, row.Height);
        TextRenderer.DrawText(e.Graphics, target.Title, Font, textBounds,
            e.Item.Selected ? SystemColors.HighlightText : SystemColors.WindowText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    private void SweepLoop()
    {
        Bitmap? scratch = null;
        while (_running)
        {
            IReadOnlyList<CaptureTarget> windows = WindowEnumerator.TopLevelWindows(Environment.ProcessId);
            var thumbs = new Dictionary<IntPtr, Bitmap>(windows.Count);
            foreach (CaptureTarget window in windows)
            {
                if (!_running)
                {
                    break;
                }

                Bitmap? thumb = PreviewCapture.TryCapture(window.Handle, ref scratch, ThumbWidth, ThumbHeight)
                    ?? PreviewCapture.IconThumb(window.Handle, ThumbWidth, ThumbHeight);
                if (thumb is not null)
                {
                    thumbs[window.Handle] = thumb;
                }
            }

            try
            {
                BeginInvoke(() => Reconcile(windows, thumbs));
            }
            catch (InvalidOperationException)
            {
                // The control is gone; nobody will dispose these but us.
                foreach (Bitmap thumb in thumbs.Values)
                {
                    thumb.Dispose();
                }

                break;
            }

            Thread.Sleep(RefreshIntervalMs);
        }

        scratch?.Dispose();
    }

    // Rows are matched by HWND and never recreated while their window lives, so check state and
    // list order survive every sweep. A checked window missing from the enumeration (cloaked, on
    // another virtual desktop) keeps its row until it is truly destroyed.
    private void Reconcile(IReadOnlyList<CaptureTarget> windows, Dictionary<IntPtr, Bitmap> thumbs)
    {
        if (IsDisposed)
        {
            foreach (Bitmap thumb in thumbs.Values)
            {
                thumb.Dispose();
            }

            return;
        }

        var alive = new HashSet<IntPtr>();
        foreach (CaptureTarget window in windows)
        {
            alive.Add(window.Handle);
        }

        BeginUpdate();
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            IntPtr hwnd = ((CaptureTarget)Items[i].Tag!).Handle;
            if (alive.Contains(hwnd) || (_checked.Contains(hwnd) && PreviewCapture.IsWindowAlive(hwnd)))
            {
                continue;
            }

            _checked.Remove(hwnd);
            if (_thumbs.Remove(hwnd, out Bitmap? dead))
            {
                dead.Dispose();
            }

            Items.RemoveAt(i);
        }

        foreach (CaptureTarget window in windows)
        {
            ListViewItem? item = FindRow(window.Handle);
            if (item is null)
            {
                Items.Add(new ListViewItem(window.Title) { Tag = window });
            }
            else if (((CaptureTarget)item.Tag!).Title != window.Title)
            {
                item.Tag = window;
                item.Text = window.Title;
            }
        }

        EndUpdate();

        foreach ((IntPtr hwnd, Bitmap thumb) in thumbs)
        {
            if (_thumbs.Remove(hwnd, out Bitmap? old))
            {
                old.Dispose();
            }

            if (FindRow(hwnd) is not null)
            {
                _thumbs[hwnd] = thumb;
            }
            else
            {
                thumb.Dispose();
            }
        }

        Invalidate();
    }

    private ListViewItem? FindRow(IntPtr hwnd)
    {
        foreach (ListViewItem item in Items)
        {
            if (((CaptureTarget)item.Tag!).Handle == hwnd)
            {
                return item;
            }
        }

        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _running = false;
            // The worker may sit inside PrintWindow against a slow target; it is a background
            // thread, so an expired join just abandons it.
            _worker?.Join(200);
            foreach (Bitmap thumb in _thumbs.Values)
            {
                thumb.Dispose();
            }

            _thumbs.Clear();
            SmallImageList?.Dispose();
        }

        base.Dispose(disposing);
    }
}
