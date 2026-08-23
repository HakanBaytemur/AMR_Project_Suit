using DwgTrueView.Cad;
using DwgTrueView.Core;
using DwgTrueView.Rendering.DirectX;

namespace DwgTrueView.App;

public sealed class MainForm : Form
{
    private readonly ShallowCadReader _reader = new();
    private readonly CadViewportControl _viewport = new();
    private readonly CheckedListBox _layers = new();
    private readonly ToolStripButton _openButton;
    private readonly ToolStripButton _zoomExtentsButton;
    private readonly ToolStripButton _gridButton;
    private readonly ToolStripStatusLabel _statusText = new();
    private readonly ToolStripStatusLabel _coordinates = new();
    private readonly ToolStripProgressBar _progress = new();
    private CancellationTokenSource? _loadCancellation;
    private bool _updatingLayers;

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
        _gridButton = CreateToolbarButton(
            "Grid",
            (sender, _) => _viewport.GridVisible =
                ((ToolStripButton)sender!).Checked);
        _gridButton.CheckOnClick = true;
        _gridButton.Checked = true;
        toolbar.Items.AddRange(
        [
            _openButton,
            new ToolStripSeparator(),
            _zoomExtentsButton,
            _gridButton,
        ]);

        var layerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(37, 39, 43),
            Padding = new Padding(10),
        };
        var layerHeader = new Label
        {
            Text = "CAD Layers",
            Dock = DockStyle.Top,
            Height = 42,
            Font = new Font("Segoe UI Semibold", 12f),
            ForeColor = Color.WhiteSmoke,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _layers.Dock = DockStyle.Fill;
        _layers.BackColor = Color.FromArgb(30, 30, 30);
        _layers.ForeColor = Color.Gainsboro;
        _layers.BorderStyle = BorderStyle.None;
        _layers.CheckOnClick = true;
        _layers.IntegralHeight = false;
        _layers.ItemCheck += OnLayerItemCheck;
        layerPanel.Controls.Add(_layers);
        layerPanel.Controls.Add(layerHeader);

        _viewport.Dock = DockStyle.Fill;
        _viewport.Cursor = Cursors.Cross;
        _viewport.WorldCursorChanged += (_, e) =>
            _coordinates.Text = $"X {e.World.X:0.###}    Y {e.World.Y:0.###}";
        _viewport.RenderFailed += exception =>
            _statusText.Text = $"DirectX error: {exception.Message}";

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 250,
            SplitterWidth = 1,
            BackColor = Color.FromArgb(70, 70, 70),
        };
        split.Panel1.Controls.Add(layerPanel);
        split.Panel2.Controls.Add(_viewport);

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

        Controls.Add(split);
        Controls.Add(toolbar);
        Controls.Add(status);
        toolbar.Dock = DockStyle.Top;
        status.Dock = DockStyle.Bottom;

        KeyDown += OnFormKeyDown;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        FormClosing += (_, _) =>
        {
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
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

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadCancellation.Token;
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
            _viewport.LoadDrawing(drawing);
            PopulateLayers(drawing);
            Text = $"DWG TrueView Lite V2 — {Path.GetFileName(path)}";
            _statusText.Text =
                $"{drawing.SegmentCount:N0} segments  |  "
                + $"{drawing.VertexBytes / 1024d / 1024d:N1} MiB GPU vertices  |  "
                + $"{drawing.SkippedEntityCount:N0} unsupported/hidden";
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
                exception.Message,
                "DWG/DXF load error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open DWG or DXF",
            Filter = "AutoCAD drawings (*.dwg;*.dxf)|*.dwg;*.dxf|DWG (*.dwg)|*.dwg|DXF (*.dxf)|*.dxf",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _ = OpenPathAsync(dialog.FileName);
        }
    }

    private void PopulateLayers(PackedCadDrawing drawing)
    {
        _updatingLayers = true;
        _layers.BeginUpdate();
        _layers.Items.Clear();
        ReadOnlySpan<CadLayer> layers = drawing.Layers.Span;
        for (int index = 0; index < layers.Length; index++)
        {
            CadLayer layer = layers[index];
            int itemIndex = _layers.Items.Add(new LayerListItem(layer.Id, layer.Name));
            _layers.SetItemChecked(itemIndex, layer.IsInitiallyVisible);
        }
        _layers.EndUpdate();
        _updatingLayers = false;
    }

    private void OnLayerItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_updatingLayers || _layers.Items[e.Index] is not LayerListItem item)
        {
            return;
        }
        _viewport.SetLayerVisible(item.Id, e.NewValue == CheckState.Checked);
    }

    private void SetLoading(bool loading)
    {
        _openButton.Enabled = !loading;
        _zoomExtentsButton.Enabled = !loading;
        _layers.Enabled = !loading;
        _progress.Visible = loading;
        if (!loading)
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
        else if (e.KeyCode is Keys.Home or Keys.F)
        {
            _viewport.ZoomExtents();
            e.Handled = true;
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = HasCadFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        string? path = FirstCadFile(e.Data);
        if (path is not null)
        {
            _ = OpenPathAsync(path);
        }
    }

    private static bool HasCadFile(IDataObject? data) => FirstCadFile(data) is not null;

    private static string? FirstCadFile(IDataObject? data)
    {
        if (data?.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return null;
        }
        return files.FirstOrDefault(
            path =>
            {
                string extension = Path.GetExtension(path);
                return extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase);
            });
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

    private sealed record LayerListItem(int Id, string Name)
    {
        public override string ToString() => Name;
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
