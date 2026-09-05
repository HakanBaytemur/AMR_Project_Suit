using System.Drawing.Printing;
using System.Numerics;
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
    private readonly RibbonMenu _ribbon = new();
    private readonly RecentFileList _recent = new();
    private readonly ViewCamera2D _emptyCamera = new();
    private readonly ToolStripStatusLabel _statusText = new();
    private readonly ToolStripStatusLabel _coordinates = new();
    private readonly ToolStripProgressBar _progress = new();
    private readonly UndoStack _undo = new();
    private readonly CadAliasBuffer _aliases = new();
    private LayerPropertiesForm? _layerForm;
    private CancellationTokenSource _shutdown = new();
    private int _loadsInFlight;
    private bool _applyingUndo;

    public MainForm()
    {
        Text = ProductInfo.Name;
        ShowIcon = true;
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                Icon = Icon.ExtractAssociatedIcon(exe);
            }
        }
        catch (ArgumentException)
        {
        }
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 600);
        Size = new Size(1440, 900);
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;
        AllowDrop = true;

        _ribbon.BindCommands(
            OnOpenClicked,
            OnSaveClicked,
            OnLayerPropertiesClicked,
            OnCopyClicked,
            OnPasteClicked,
            (_, _) => _viewport.ZoomExtents(),
            OnGridChanged,
            OnZoomWindowChanged);
        _ribbon.SetRecentFiles(_recent.Paths, path => _ = OpenPathAsync(path));
        _ribbon.UndoClicked += OnUndoClicked;
        _ribbon.RedoClicked += OnRedoClicked;
        _undo.Changed += (_, _) =>
        {
            _ribbon.SetUndoEnabled(_undo.CanUndo);
            _ribbon.SetRedoEnabled(_undo.CanRedo);
        };
        _ribbon.SetUndoEnabled(false);
        _ribbon.SetRedoEnabled(false);
        _viewport.CameraUndoCommitted += OnCameraUndoCommitted;
        _viewport.ZoomWindowArmedChanged += armed =>
        {
            if (_ribbon.ZoomWindowButton.Checked != armed)
            {
                _ribbon.ZoomWindowButton.Checked = armed;
            }
        };

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
        _statusText.Text = "Ready — wheel zoom, middle-button pan, Home or middle double-click to fit";
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
        Controls.Add(_ribbon);
        status.Dock = DockStyle.Bottom;
        _tabs.Dock = DockStyle.Top;
        _ribbon.Dock = DockStyle.Top;
        _ribbon.AttachHost(this);
        WindowState = FormWindowState.Maximized;

        _aliases.Matched += OnAliasMatched;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        Activated += (_, _) => TaskbarHighlight.StopFlash(this);
        HandleCreated += (_, _) =>
        {
            DarkTitleBar.Apply(this);
            CaptionFrame.NotifyChanged(this);
        };
        FormClosing += (_, _) =>
        {
            _shutdown.Cancel();
            if (_layerForm is { IsDisposed: false })
            {
                _layerForm.Close();
            }
            _aliases.Dispose();
        };
    }

    protected override void WndProc(ref Message message)
    {
        if (CaptionFrame.Process(this, _ribbon, ref message))
        {
            return;
        }

        base.WndProc(ref message);
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
                int percent = Math.Clamp(value.Percent, 0, 100);
                _progress.Value = percent;
                TaskbarHighlight.SetProgress(this, percent);
                _statusText.Text = value.TotalEntities > 0
                    ? $"{value.Stage} — {value.ProcessedEntities:N0}/{value.TotalEntities:N0}"
                    : value.Stage;
            });
        bool succeeded = false;
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
                DrawingWorkspace tab = _workspace.Add(drawing);
                int index = _workspace.IndexOf(tab.Id);
                PresentActive(fitExtents: true);
                _recent.Remember(path);
                _ribbon.SetRecentFiles(_recent.Paths, recentPath => _ = OpenPathAsync(recentPath));
                Record("Open",
                    () =>
                    {
                        if (_workspace.Close(tab.Id))
                        {
                            PresentActive(fitExtents: false);
                        }
                    },
                    () =>
                    {
                        _workspace.Insert(tab, index, activate: true);
                        PresentActive(fitExtents: false);
                    });
            }
            if (InvokeRequired)
            {
                Invoke(commit);
            }
            else
            {
                commit();
            }
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = "Load cancelled";
        }
        catch (Exception exception)
        {
            _statusText.Text = "Could not open drawing";
            TaskbarHighlight.NotifyError(this);
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
            if (succeeded)
            {
                TaskbarHighlight.NotifySuccess(this);
            }
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
            PackedCadDrawing drawing = tab.Drawing;
            _statusText.Text =
                $"{drawing.SegmentCount:N0} segments  |  "
                + $"{drawing.VertexBytes / 1024d / 1024d:N1} MiB GPU vertices  |  "
                + $"{drawing.SkippedEntityCount:N0} unsupported/hidden";
            return;
        }

        _viewport.PresentSession(null, _emptyCamera, [], fitExtents: false);
        BindLayers(null);
        _statusText.Text = "Ready — wheel zoom, middle-button pan, Home or middle double-click to fit";
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
        DrawingWorkspace? tab = _workspace.Find(id);
        int index = _workspace.IndexOf(id);
        if (tab is null || index < 0)
        {
            return;
        }
        if (_workspace.Close(id))
        {
            Record("Close tab",
                () =>
                {
                    _workspace.Insert(tab, index, activate: true);
                    PresentActive(fitExtents: false);
                },
                () =>
                {
                    if (_workspace.Close(tab.Id))
                    {
                        PresentActive(fitExtents: false);
                    }
                });
            PresentActive(fitExtents: false);
        }
    }

    private void OnTabMoved(Guid id, int index)
    {
        int from = _workspace.IndexOf(id);
        if (from < 0 || !_workspace.Move(id, index))
        {
            return;
        }
        Record("Tab order", () => _workspace.Move(id, from), () => _workspace.Move(id, index));
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
            _ribbon.LayersButton.Checked = false;
            return;
        }
        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }
        BindLayers(_workspace.Active);
        form.Show(this);
        form.Activate();
        _ribbon.LayersButton.Checked = true;
    }

    private LayerPropertiesForm EnsureLayerForm()
    {
        if (_layerForm is { IsDisposed: false })
        {
            return _layerForm;
        }
        var form = new LayerPropertiesForm();
        form.VisibilityChanged += (layerId, visible) =>
        {
            bool previous = _viewport.IsLayerVisible(layerId);
            _viewport.SetLayerVisible(layerId, visible);
            if (previous == visible)
            {
                return;
            }
            Record("Layer",
                () => ApplyLayerVisibility(layerId, previous),
                () => ApplyLayerVisibility(layerId, visible));
        };
        form.FormClosed += (_, _) =>
        {
            _ribbon.LayersButton.Checked = false;
            _layerForm = null;
        };
        form.VisibleChanged += (_, _) =>
        {
            if (_layerForm is { IsDisposed: false })
            {
                _ribbon.LayersButton.Checked = _layerForm.Visible;
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

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_workspace.Active is not { } tab)
        {
            MessageBox.Show(
                this,
                "Open a drawing before saving.",
                "Save",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string source = tab.SourcePath;
        string extension = Path.GetExtension(source);
        using var dialog = new SaveFileDialog
        {
            Title = "Save drawing copy",
            FileName = tab.FileName,
            Filter = extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase)
                ? "DXF (*.dxf)|*.dxf|DWG (*.dwg)|*.dwg"
                : "DWG (*.dwg)|*.dwg|DXF (*.dxf)|*.dxf",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            File.Copy(source, dialog.FileName, overwrite: true);
            _statusText.Text = $"Saved a copy of the source file — {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Save error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnCopyClicked(object? sender, EventArgs e)
    {
        Rectangle screen = _viewport.RectangleToScreen(_viewport.ClientRectangle);
        if (screen.Width <= 1 || screen.Height <= 1)
        {
            return;
        }
        using var bitmap = new Bitmap(screen.Width, screen.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
        }
        Clipboard.SetImage((Image)bitmap.Clone());
        _statusText.Text = "Copied viewport to clipboard";
    }

    private void OnPasteClicked(object? sender, EventArgs e)
    {
        if (Clipboard.ContainsFileDropList())
        {
            foreach (string? path in Clipboard.GetFileDropList())
            {
                if (path is not null)
                {
                    _ = OpenPathAsync(path);
                }
            }
            return;
        }
        if (Clipboard.ContainsText())
        {
            string text = Clipboard.GetText().Trim().Trim('"');
            if (File.Exists(text))
            {
                _ = OpenPathAsync(text);
                return;
            }
        }
        _statusText.Text = "Clipboard has no DWG/DXF file to paste";
    }

    private void OnUndoClicked(object? sender, EventArgs e)
    {
        _viewport.CommitPendingCameraUndo();
        if (!_undo.CanUndo)
        {
            return;
        }

        string name = _undo.NextName ?? "action";
        _applyingUndo = true;
        try
        {
            _undo.TryUndo();
        }
        finally
        {
            _applyingUndo = false;
        }
        _statusText.Text = $"Undid {name}";
    }

    private void OnRedoClicked(object? sender, EventArgs e)
    {
        if (!_undo.CanRedo)
        {
            return;
        }

        string name = _undo.NextRedoName ?? "action";
        _applyingUndo = true;
        try
        {
            _undo.TryRedo();
        }
        finally
        {
            _applyingUndo = false;
        }
        _statusText.Text = $"Redid {name}";
    }

    private void RestoreView(ViewCamera2D camera, Vector2 center, float unitsPerPixel)
    {
        camera.Restore(center, unitsPerPixel);
        if (ReferenceEquals(_viewport.Camera, camera))
        {
            _viewport.RefreshView();
        }
    }

    private void ApplyLayerVisibility(int layerId, bool visible)
    {
        _viewport.SetLayerVisible(layerId, visible);
        if (_workspace.Active is { } tab
            && (uint)layerId < (uint)tab.LayerVisibility.Length)
        {
            tab.LayerVisibility[layerId] = visible;
        }
        BindLayers(_workspace.Active);
    }

    private void OnCameraUndoCommitted(object? sender, CameraUndoEventArgs e)
    {
        ViewCamera2D camera = e.Camera;
        Vector2 center = e.PreviousCenter;
        float unitsPerPixel = e.PreviousUnitsPerPixel;
        Vector2 afterCenter = camera.Center;
        float afterScale = camera.UnitsPerPixel;
        Record(
            "View",
            () => RestoreView(camera, center, unitsPerPixel),
            () => RestoreView(camera, afterCenter, afterScale));
    }

    private void OnGridChanged(object? sender, EventArgs e)
    {
        bool next = _ribbon.GridButton.Checked;
        bool previous = _viewport.GridVisible;
        _viewport.GridVisible = next;
        if (previous == next)
        {
            return;
        }
        Record(
            "Grid",
            () =>
            {
                _viewport.GridVisible = previous;
                _ribbon.GridButton.Checked = previous;
            },
            () =>
            {
                _viewport.GridVisible = next;
                _ribbon.GridButton.Checked = next;
            });
    }

    private void Record(string name, Action undo, Action redo)
    {
        if (_applyingUndo)
        {
            return;
        }
        _undo.Push(new DelegateUndoAction(name, undo, redo));
    }

    private void OnZoomWindowChanged(object? sender, EventArgs e)
    {
        if (_ribbon.ZoomWindowButton.Checked)
        {
            _viewport.BeginZoomWindow();
            _statusText.Text = "Zoom Window — drag a rectangle on the canvas, Esc or right-click to cancel";
            return;
        }
        if (_viewport.ZoomWindowArmed)
        {
            _viewport.CancelZoomWindow();
        }
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
        if (busy)
        {
            TaskbarHighlight.SetProgress(this, _progress.Value);
        }
        else
        {
            _progress.Value = 0;
            TaskbarHighlight.Clear(this);
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

        if (CadShortcuts.IsTextEditing(ActiveControl))
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        Keys modifiers = keyData & Keys.Modifiers;
        Keys key = keyData & Keys.KeyCode;
        if (modifiers == Keys.Control)
        {
            _aliases.Clear();
            switch (key)
            {
                case Keys.N:
                    OnOpenClicked(this, EventArgs.Empty);
                    return true;
                case Keys.O:
                    OnOpenClicked(this, EventArgs.Empty);
                    return true;
                case Keys.S:
                    OnSaveClicked(this, EventArgs.Empty);
                    return true;
                case Keys.P:
                    OnPrintClicked();
                    return true;
                case Keys.Z:
                    OnUndoClicked(this, EventArgs.Empty);
                    return true;
                case Keys.Y:
                    OnRedoClicked(this, EventArgs.Empty);
                    return true;
                case Keys.C:
                    OnCopyClicked(this, EventArgs.Empty);
                    return true;
                case Keys.V:
                    OnPasteClicked(this, EventArgs.Empty);
                    return true;
                case Keys.X:
                    AnnounceCommand(_ribbon.CutButton);
                    return true;
                case Keys.W:
                    if (_workspace.Active is { } tab)
                    {
                        OnTabClosed(tab.Id);
                    }
                    return true;
            }
        }

        if (modifiers == (Keys.Control | Keys.Shift) && key == Keys.Z)
        {
            _aliases.Clear();
            OnRedoClicked(this, EventArgs.Empty);
            return true;
        }

        if (modifiers == Keys.None)
        {
            switch (key)
            {
                case Keys.Home:
                    _aliases.Clear();
                    _viewport.ZoomExtents();
                    return true;
                case Keys.F3:
                    _aliases.Clear();
                    ToggleObjectSnap();
                    return true;
                case Keys.F7:
                    _aliases.Clear();
                    _ribbon.GridButton.Checked = !_ribbon.GridButton.Checked;
                    return true;
                case Keys.F8:
                    _aliases.Clear();
                    _ribbon.OrthogonalSnapButton.Checked = !_ribbon.OrthogonalSnapButton.Checked;
                    _statusText.Text = _ribbon.OrthogonalSnapButton.Checked
                        ? "Orthogonal Snap on [F8]"
                        : "Orthogonal Snap off [F8]";
                    return true;
            }

            if (_aliases.Feed(key))
            {
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnAliasMatched(string alias)
    {
        string? name = CadShortcuts.CommandName(alias);
        if (name is null)
        {
            return;
        }

        ToolStripButton? button = _ribbon.FindCommand(name);
        if (button is not null)
        {
            AnnounceCommand(button);
        }
    }

    private void ToggleObjectSnap()
    {
        bool next = !(_ribbon.NodeSnapButton.Checked || _ribbon.GeometricSnapButton.Checked);
        _ribbon.NodeSnapButton.Checked = next;
        _ribbon.GeometricSnapButton.Checked = next;
        _statusText.Text = next ? "Object snap on [F3]" : "Object snap off [F3]";
    }

    private void AnnounceCommand(ToolStripButton button)
    {
        string title = button.Text ?? "Command";
        string shortcut = button.Tag is CommandTip tip && !string.IsNullOrWhiteSpace(tip.Shortcut)
            ? $" [{tip.Shortcut}]"
            : string.Empty;
        string body = button.Tag is CommandTip command && !string.IsNullOrWhiteSpace(command.Description)
            ? command.Description
            : "This command will be added here.";
        _statusText.Text = $"{title}{shortcut} — {body}";
    }

    private void OnPrintClicked()
    {
        Rectangle screen = _viewport.RectangleToScreen(_viewport.ClientRectangle);
        if (screen.Width <= 1 || screen.Height <= 1)
        {
            return;
        }

        using var bitmap = new Bitmap(screen.Width, screen.Height);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
        }

        using var document = new PrintDocument();
        document.DocumentName = _workspace.Active?.FileName ?? ProductInfo.Name;
        document.PrintPage += (_, e) =>
        {
            if (e.Graphics is null)
            {
                return;
            }

            Rectangle margin = e.MarginBounds;
            float scale = Math.Min(
                margin.Width / (float)bitmap.Width,
                margin.Height / (float)bitmap.Height);
            int width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            int height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            var dest = new Rectangle(
                margin.X + (margin.Width - width) / 2,
                margin.Y + (margin.Height - height) / 2,
                width,
                height);
            e.Graphics.DrawImage(bitmap, dest);
            e.HasMorePages = false;
        };

        using var dialog = new PrintDialog
        {
            Document = document,
            UseEXDialog = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        document.Print();
        _statusText.Text = "Print — sent the current viewport to the printer.";
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

    private static string FormatLoadError(Exception exception)
    {
        Exception current = exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.FirstOrDefault() ?? aggregate
            : exception;
        return string.IsNullOrWhiteSpace(current.InnerException?.Message)
            ? current.Message
            : $"{current.Message}{Environment.NewLine}{Environment.NewLine}{current.InnerException.Message}";
    }
}
