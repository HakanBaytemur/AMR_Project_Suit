using DwgTrueView.Core;

namespace DwgTrueView.App;

/// <summary>
/// Compact layer table: bulb toggles live visibility; letter keys jump to
/// matching names when the list has focus.
/// </summary>
internal sealed class LayerPropertiesPanel : Panel
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = new();
    private readonly Label _current = new();
    private readonly Image _bulbOn;
    private readonly Image _bulbOff;
    private readonly Dictionary<int, bool> _visibility = [];
    private bool _updating;
    private CadLayer[] _layers = [];

    public LayerPropertiesPanel()
    {
        _bulbOn = CreateBulb(on: true);
        _bulbOff = CreateBulb(on: false);
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(37, 39, 43);
        Padding = new Padding(8, 6, 8, 8);

        _current.AutoSize = false;
        _current.Height = 22;
        _current.Dock = DockStyle.Top;
        _current.ForeColor = Color.Gainsboro;
        _current.TextAlign = ContentAlignment.MiddleLeft;
        _current.Text = "Current layer: —";

        _search.Dock = DockStyle.Fill;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.BackColor = Color.FromArgb(30, 30, 30);
        _search.ForeColor = Color.Gainsboro;
        _search.PlaceholderText = "Search for layer";
        _search.TextChanged += (_, _) => ApplyFilter();

        var searchHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(0, 0, 0, 4),
            BackColor = Color.FromArgb(37, 39, 43),
        };
        searchHost.Controls.Add(_search);

        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Color.FromArgb(30, 30, 30);
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.EditMode = DataGridViewEditMode.EditProgrammatically;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowTemplate.Height = 24;
        _grid.ColumnHeadersHeight = 24;
        _grid.StandardTab = true;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(48, 51, 56),
            ForeColor = Color.Gainsboro,
            SelectionBackColor = Color.FromArgb(48, 51, 56),
            Font = new Font("Segoe UI Semibold", 8.5f),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro,
            SelectionBackColor = Color.FromArgb(58, 68, 80),
            SelectionForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f),
        };
        _grid.GridColor = Color.FromArgb(52, 56, 61);
        _grid.CellMouseClick += OnCellMouseClick;
        _grid.CellMouseMove += OnCellMouseMove;
        _grid.PreviewKeyDown += OnGridPreviewKeyDown;
        _grid.KeyDown += OnGridKeyDown;

        _grid.Columns.Add(new DataGridViewImageColumn
        {
            Name = "Visible",
            HeaderText = "On",
            Width = 44,
            FillWeight = 18,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            ImageLayout = DataGridViewImageCellLayout.Zoom,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "Name",
            FillWeight = 82,
        });

        Controls.Add(_grid);
        Controls.Add(searchHost);
        Controls.Add(_current);
    }

    public event Action<int, bool>? VisibilityChanged;

    public void Bind(IReadOnlyList<CadLayer> layers, bool[]? visibility = null)
    {
        _layers = layers.ToArray();
        _visibility.Clear();
        foreach (CadLayer layer in _layers)
        {
            _visibility[layer.Id] = visibility is not null
                && (uint)layer.Id < (uint)visibility.Length
                    ? visibility[layer.Id]
                    : layer.IsInitiallyVisible;
        }
        _search.Clear();
        RebuildRows();
        CadLayer? current = _layers.FirstOrDefault(static layer => layer.Name == "0")
            ?? (_layers.Length > 0 ? _layers[0] : null);
        _current.Text = $"Current layer: {current?.Name ?? "—"}";
    }

    public void ClearLayers()
    {
        _layers = [];
        _visibility.Clear();
        _grid.Rows.Clear();
        _current.Text = "Current layer: —";
    }

    private void RebuildRows()
    {
        _updating = true;
        _grid.Rows.Clear();
        string filter = _search.Text.Trim();
        foreach (CadLayer layer in _layers)
        {
            if (filter.Length > 0
                && layer.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            bool visible = _visibility.GetValueOrDefault(layer.Id, layer.IsInitiallyVisible);
            int rowIndex = _grid.Rows.Add(visible ? _bulbOn : _bulbOff, layer.Name);
            DataGridViewRow row = _grid.Rows[rowIndex];
            row.Tag = layer;
            row.Cells[0].Tag = visible;
        }
        _updating = false;
    }

    private void ApplyFilter()
    {
        if (!_updating)
        {
            RebuildRows();
        }
    }

    private void OnCellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (_updating || e.RowIndex < 0 || e.ColumnIndex != 0)
        {
            return;
        }
        DataGridViewRow row = _grid.Rows[e.RowIndex];
        if (row.Tag is not CadLayer layer)
        {
            return;
        }
        bool visible = row.Cells[0].Tag is true;
        bool next = !visible;
        row.Cells[0].Tag = next;
        row.Cells[0].Value = next ? _bulbOn : _bulbOff;
        _visibility[layer.Id] = next;
        VisibilityChanged?.Invoke(layer.Id, next);
    }

    private void OnCellMouseMove(object? sender, DataGridViewCellMouseEventArgs e)
    {
        _grid.Cursor = e.RowIndex >= 0 && e.ColumnIndex == 0
            ? Cursors.Hand
            : Cursors.Default;
    }

    private void OnGridPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        if (e.KeyCode is >= Keys.A and <= Keys.Z
            or >= Keys.D0 and <= Keys.D9
            or >= Keys.NumPad0 and <= Keys.NumPad9
            or Keys.OemMinus
            or Keys.Subtract)
        {
            e.IsInputKey = true;
        }
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (PrefixFromKey(e) is not char prefix)
        {
            return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
        JumpToPrefix(prefix);
    }

    private static char? PrefixFromKey(KeyEventArgs e)
    {
        if (e.Control || e.Alt)
        {
            return null;
        }
        return e.KeyCode switch
        {
            >= Keys.A and <= Keys.Z => (char)('A' + (e.KeyCode - Keys.A)),
            >= Keys.D0 and <= Keys.D9 => (char)('0' + (e.KeyCode - Keys.D0)),
            >= Keys.NumPad0 and <= Keys.NumPad9 => (char)('0' + (e.KeyCode - Keys.NumPad0)),
            Keys.OemMinus or Keys.Subtract => '-',
            Keys.Oemplus => '_',
            _ => null,
        };
    }

    private void JumpToPrefix(char prefix)
    {
        int count = _grid.Rows.Count;
        if (count == 0)
        {
            return;
        }
        int start = _grid.CurrentCell?.RowIndex ?? -1;
        for (int step = 1; step <= count; step++)
        {
            int index = (start + step) % count;
            if (_grid.Rows[index].Tag is not CadLayer layer
                || layer.Name.Length == 0
                || !layer.Name.StartsWith(prefix.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            _grid.ClearSelection();
            _grid.Rows[index].Selected = true;
            _grid.CurrentCell = _grid.Rows[index].Cells[1];
            try
            {
                _grid.FirstDisplayedScrollingRowIndex = index;
            }
            catch (InvalidOperationException)
            {
            }
            return;
        }
    }

    private static Bitmap CreateBulb(bool on)
    {
        var bitmap = new Bitmap(18, 18);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        if (on)
        {
            using var fill = new SolidBrush(Color.FromArgb(255, 206, 64));
            graphics.FillEllipse(fill, 3, 2, 12, 12);
            graphics.FillRectangle(fill, 7, 13, 4, 3);
        }
        else
        {
            using var pen = new Pen(Color.FromArgb(120, 120, 120), 1.5f);
            graphics.DrawEllipse(pen, 3, 2, 12, 12);
            graphics.DrawRectangle(pen, 7, 13, 4, 3);
        }
        return bitmap;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bulbOn.Dispose();
            _bulbOff.Dispose();
        }
        base.Dispose(disposing);
    }
}
