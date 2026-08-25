using DwgTrueView.Cad;
using DwgTrueView.Core;
using DwgTrueView.Rendering.DirectX;

namespace DwgTrueView.App;

public sealed class MainForm : Form
{
    private readonly ShallowCadReader _reader = new();
    private readonly CadViewportControl _viewport = new();
    private readonly WorkspaceTabCollection _workspace = new();
    private readonly WorkspaceTabStrip _tabs = new();
    private readonly ViewCamera2D _emptyCamera = new();
    private readonly ToolStripButton _openButton;
    private readonly ToolStripButton _zoomExtentsButton;
    private readonly ToolStripButton _gridButton;
    private readonly ToolStripButton _layersButton;
    private readonly ToolStripStatusLabel _statusText = new();
    private readonly ToolStripStatusLabel _coordinates = new();
    private readonly ToolStripProgressBar _progress = new();
    private LayerPropertiesForm? _layerForm;
    private CancellationTokenSource _shutdown = new();
    private int _loadsInFlight;

    public MainForm()
    {
        Text = "DWG TrueView Lite V2";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 600);
        Size = new Size(1440, 900);
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;
        AllowDrop = true;

        var toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = Color.FromArgb(42, 45, 49),
            ForeColor = Color.WhiteSmoke,
            Renderer = new DarkToolStripRenderer(),
            Padding = new Padding(8, 5, 8, 5),
            AutoSize = true,
        };
        _openButton = CreateToolbarButton("Open DWG/DXF", OnOpenClicked);
        _zoomExtentsButton = CreateToolbarButton("Zoom Extents", (_, _) => _viewport.ZoomExtents());
        _gridButton = CreateToolbarButton("Grid", static (_, _) => { });
        _gridButton.CheckOnClick = true;
        _gridButton.Checked = true;
        _gridButton.CheckedChanged += (_, _) => _viewport.GridVisible = _gridButton.Checked;
        _layersButton = CreateToolbarButton("Layer Properties", OnLayerPropertiesClicked);
        toolbar.Items.AddRange(
        [
            _openButton,
            new ToolStripSeparator(),
            _zoomExtentsButton,
            _gridButton,
            _layersButton,
        ]);

        _tabs.TabSelected += OnTabSelected;
        _tabs.TabClosed += OnTabClosed;
        _tabs.TabMoved += OnTabMoved;
        _tabs.NewTabClicked += OnOpenClicked;
        _tabs.DragEnter += OnDragEnter;
        _tabs.DragDrop += OnDragDrop;
        _workspace.Changed += (_, _) =>
            _tabs.Bind(_workspace.Tabs, _workspace.Active?.Id);

        _viewport.Dock = DockStyle.Fill;
        _viewport.Cursor = Cursors.Cross;
        _viewport.WorldCursorChanged += (_, e) =>
            _coordinates.Text = $"X {e.World.X:0.###}    Y {e.World.Y:0.###}";
        _viewport.RenderFailed += exception =>
            _statusText.Text = $"DirectX error: {exception.Message}";

        var status = new StatusStrip
        {
            BackColor = Color.FromArgb(42, 45, 49),
            ForeColor = Color.Gainsboro,
            SizingGrip = false,
        };
        _statusText.Spring = true;
        _statusText.TextAlign = ContentAlignment.MiddleLeft;
        _statusText.Text = "Ready — wheel zoom, middle-button pan, Home/F zoom extents";
        _coordinates.AutoSize = false;
        _coordinates.Width = 220;
        _coordinates.TextAlign = ContentAlignment.MiddleRight;
        _coordinates.Text = "X 0    Y 0";
        _progress.Visible = false;
        _progress.Width = 150;
        status.Items.AddRange([_statusText, _progress, _coordinates]);

        Controls.Add(_viewport);
        Controls.Add(status);
        Controls.Add(_tabs);
        Controls.Add(toolbar);
        status.Dock = DockStyle.Bottom;
        _tabs.Dock = DockStyle.Top;
        toolbar.Dock = DockStyle.Top;

        KeyDown += OnFormKeyDown;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        FormClosing += (_, _) =>
        {
            _shutdown.Cancel();
            if (_layerForm is { IsDisposed: false })
            {
                _layerForm.Close();
            }
        };
    }

    public async Task OpenPathAsync(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CancellationToken cancellationToken = _shutdown.IsCancellationRequested
            ? new CancellationToken(canceled: true)
            : _shutdown.Token;
        SetLoading(true);
        _statusText.Text = $"Opening {Path.GetFileName(path)}…";
        var progress = new Progress<CadLoadProgress>(
            value =>
            {
                _progress.Value = Math.Clamp(value.Percent, 0, 100);
                _statusText.Text = value.TotalEntities > 0
                    ? $"{value.Stage} — {value.ProcessedEntities:N0}/{value.TotalEntities:N0}"
                    : value.Stage;
            });
        try
        {
            PackedCadDrawing drawing = await _reader.ReadAsync(
                path,
                progress: progress,
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDisposed)
            {
                return;
            }
            void commit()
            {
                _workspace.Add(drawing);
                PresentActive(fitExtents: true);
            }
            if (InvokeRequired)
            {
                Invoke(commit);
            }
            else
            {
                commit();
            }
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = "Load cancelled";
        }
        catch (Exception exception)
        {
            _statusText.Text = "Could not open drawing";
            MessageBox.Show(
                this,
                FormatLoadError(exception),
                "DWG/DXF load error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void PresentActive(bool fitExtents)
    {
        if (_workspace.Active is { } tab)
        {
            _viewport.PresentSession(
                tab.Drawing,
                tab.Camera,
                tab.LayerVisibility,
                fitExtents);
            BindLayers(tab);
            Text = $"DWG TrueView Lite V2 — {tab.FileName}";
            PackedCadDrawing drawing = tab.Drawing;
            _statusText.Text =
                $"{drawing.SegmentCount:N0} segments  |  "
                + $"{drawing.VertexBytes / 1024d / 1024d:N1} MiB GPU vertices  |  "
                + $"{drawing.SkippedEntityCount:N0} unsupported/hidden";
            return;
        }

        _viewport.PresentSession(null, _emptyCamera, [], fitExtents: false);
        BindLayers(null);
        Text = "DWG TrueView Lite V2";
        _statusText.Text = "Ready — wheel zoom, middle-button pan, Home/F zoom extents";
    }

    private void OnTabSelected(Guid id)
    {
        if (_workspace.Activate(id))
        {
            PresentActive(fitExtents: false);
        }
    }

    private void OnTabClosed(Guid id)
    {
        if (_workspace.Close(id))
        {
            PresentActive(fitExtents: false);
        }
    }

    private void OnTabMoved(Guid id, int index)
    {
        _workspace.Move(id, index);
    }

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open DWG or DXF",
            Filter = "AutoCAD drawings (*.dwg;*.dxf)|*.dwg;*.dxf|DWG (*.dwg)|*.dwg|DXF (*.dxf)|*.dxf",
            CheckFileExists = true,
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        foreach (string path in dialog.FileNames)
        {
            _ = OpenPathAsync(path);
        }
    }

    private void BindLayers(DrawingWorkspace? tab)
    {
        if (_layerForm is not { IsDisposed: false })
        {
            return;
        }
        if (tab is null)
        {
            _layerForm.ClearLayers();
            return;
        }
        _layerForm.Bind(tab.Drawing.Layers.ToArray(), tab.LayerVisibility);
    }

    private void OnLayerPropertiesClicked(object? sender, EventArgs e)
    {
        LayerPropertiesForm form = EnsureLayerForm();
        if (form.Visible)
        {
            form.Hide();
            _layersButton.Checked = false;
            return;
        }
        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }
        BindLayers(_workspace.Active);
        form.Show(this);
        form.Activate();
        _layersButton.Checked = true;
    }

    private LayerPropertiesForm EnsureLayerForm()
    {
        if (_layerForm is { IsDisposed: false })
        {
            return _layerForm;
        }
        var form = new LayerPropertiesForm();
        form.VisibilityChanged += (layerId, visible) =>
            _viewport.SetLayerVisible(layerId, visible);
        form.FormClosed += (_, _) =>
        {
            _layersButton.Checked = false;
            _layerForm = null;
        };
        form.VisibleChanged += (_, _) =>
        {
            if (_layerForm is { IsDisposed: false })
            {
                _layersButton.Checked = _layerForm.Visible;
            }
        };
        _layerForm = form;
        BindLayers(_workspace.Active);
        Point origin = _viewport.IsHandleCreated
            ? _viewport.PointToScreen(new Point(24, 24))
            : PointToScreen(new Point(24, 80));
        form.Location = origin;
        return form;
    }

    private void SetLoading(bool loading)
    {
        if (loading)
        {
            _loadsInFlight++;
        }
        else
        {
            _loadsInFlight = Math.Max(0, _loadsInFlight - 1);
        }
        bool busy = _loadsInFlight > 0;
        _progress.Visible = busy;
        if (!busy)
        {
            _progress.Value = 0;
        }
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.O)
        {
            OnOpenClicked(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.W)
        {
            if (_workspace.Active is { } tab)
            {
                OnTabClosed(tab.Id);
            }
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Home or Keys.F)
        {
            _viewport.ZoomExtents();
            e.Handled = true;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is (Keys.Control | Keys.Tab) or (Keys.Control | Keys.Shift | Keys.Tab))
        {
            int delta = keyData.HasFlag(Keys.Shift) ? -1 : 1;
            if (_workspace.ActivateRelative(delta))
            {
                PresentActive(fitExtents: false);
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = CadFiles(e.Data).Any() ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        foreach (string path in CadFiles(e.Data))
        {
            _ = OpenPathAsync(path);
        }
    }

    private static IEnumerable<string> CadFiles(IDataObject? data)
    {
        if (data?.GetData(DataFormats.FileDrop) is not string[] files)
        {
            yield break;
        }
        foreach (string path in files)
        {
            string extension = Path.GetExtension(path);
            if (extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    private static ToolStripButton CreateToolbarButton(
        string text,
        EventHandler onClick)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            AutoSize = true,
            Margin = new Padding(3),
            Padding = new Padding(10, 4, 10, 4),
            ForeColor = Color.WhiteSmoke,
        };
        button.Click += onClick;
        return button;
    }

    private static string FormatLoadError(Exception exception)
    {
        Exception current = exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.FirstOrDefault() ?? aggregate
            : exception;
        return string.IsNullOrWhiteSpace(current.InnerException?.Message)
            ? current.Message
            : $"{current.Message}{Environment.NewLine}{Environment.NewLine}{current.InnerException.Message}";
    }

    private sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer()
            : base(new DarkColorTable())
        {
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Color.FromArgb(42, 45, 49);
        public override Color ToolStripGradientMiddle => Color.FromArgb(42, 45, 49);
        public override Color ToolStripGradientEnd => Color.FromArgb(42, 45, 49);
        public override Color ButtonSelectedHighlight => Color.FromArgb(65, 72, 80);
        public override Color ButtonSelectedGradientBegin => Color.FromArgb(65, 72, 80);
        public override Color ButtonSelectedGradientEnd => Color.FromArgb(65, 72, 80);
        public override Color ButtonCheckedGradientBegin => Color.FromArgb(0, 120, 215);
        public override Color ButtonCheckedGradientEnd => Color.FromArgb(0, 100, 190);
        public override Color SeparatorDark => Color.FromArgb(75, 78, 82);
        public override Color SeparatorLight => Color.FromArgb(75, 78, 82);
    }
}
