namespace DwgTrueView.App;

/// <summary>
/// Dark CAD chrome: the mini toolbar shares the title bar; grouped
/// commands including simulation live on the main ribbon.
/// </summary>
internal sealed class RibbonMenu : Panel
{
    private static readonly Color TabBarColor = Color.FromArgb(0x22, 0x29, 0x33);
    private static readonly Color CommandBarColor = Color.FromArgb(0x3B, 0x44, 0x53);
    private static readonly Color BorderColor = Color.FromArgb(0x22, 0x29, 0x33);

    private readonly RibbonTabBar _tabBar;
    private readonly RibbonCommandBar _mainToolbar;

    public RibbonMenu()
    {
        Height = 103;
        BackColor = CommandBarColor;
        OpenButton = CreateCommand(
            "Open File",
            "Open File.svg",
            "Open a DWG or DXF drawing in a new tab.",
            compact: true,
            shortcut: "Ctrl+O");
        SaveButton = CreateCommand(
            "Save",
            "Save File.svg",
            "Save a copy of the current drawing's source file.",
            compact: true,
            shortcut: "Ctrl+S");
        RecentButton = CreateDropDown(
            "Opened Recently",
            "Opened Recently.svg",
            "Reopen a drawing from the recent-file list.");
        LayersButton = CreateCommand(
            "Layer Properties",
            "Layer.svg",
            "Show or hide drawing layers in the floating layer manager.");
        CopyButton = CreateCommand(
            "Copy",
            "Copy.svg",
            "Copy the current viewport image to the clipboard.",
            shortcut: "Ctrl+C");
        PasteButton = CreateCommand(
            "Paste",
            "Paste.svg",
            "Open a DWG or DXF file path from the clipboard.",
            shortcut: "Ctrl+V");
        ZoomExtentsButton = CreateCommand(
            "Zoom Extents",
            "Zoom Extents.svg",
            "Fit the entire drawing in the viewport. Middle-button double-click also fits.",
            shortcut: "Home");
        GridButton = CreateCommand(
            "Grid",
            "Grid.svg",
            "Show or hide the adaptive background grid.",
            shortcut: "F7");
        GridButton.CheckOnClick = true;
        GridButton.Checked = true;
        NodeSnapButton = CreateCommand(
            "Node Snap",
            "Node Snap.svg",
            "Snap to nodes. This command will be added here.",
            shortcut: "F3");
        NodeSnapButton.CheckOnClick = true;
        GeometricSnapButton = CreateCommand(
            "Geometric Snap",
            "Geometric Snap.svg",
            "Snap to geometric points. This command will be added here.",
            shortcut: "F3");
        GeometricSnapButton.CheckOnClick = true;
        OrthogonalSnapButton = CreateCommand(
            "Orthogonal Snap",
            "Orthogonal Snap.svg",
            "Constrain drawing to orthogonal directions. This command will be added here.",
            shortcut: "F8");
        OrthogonalSnapButton.CheckOnClick = true;
        StraightRouteButton = CreateCommand(
            "Straight Route",
            "Straight Route.svg",
            "Draw a straight route. This command will be added here.",
            shortcut: "L");
        MoveButton = CreateCommand(
            "Move",
            "Move.svg",
            "Move layout elements. This command will be added here.",
            shortcut: "M");
        RotateButton = CreateCommand(
            "Rotate",
            "Rotate.svg",
            "Rotate layout elements. This command will be added here.",
            shortcut: "RO");
        CopyElementsButton = CreateCommand(
            "Copy to Clipboard",
            "Copy to Clipboard.svg",
            "Copy layout elements to the clipboard. This command will be added here.",
            shortcut: "CO, CP");
        CutButton = CreateCommand(
            "Cut",
            "Cut.svg",
            "Cut layout elements. This command will be added here.",
            shortcut: "Ctrl+X");
        TrimButton = CreateCommand(
            "Trim",
            "Trim.svg",
            "Trim layout elements. This command will be added here.",
            shortcut: "TR");
        FilletButton = CreateCommand(
            "Add Radius",
            "Add Radius.svg",
            "Add a radius to a corner. This command will be added here.",
            shortcut: "F");
        ZoomWindowButton = CreateCommand(
            "Zoom Window",
            "Zoom Window.svg",
            "Drag a rectangle on the canvas to zoom into that area.");
        ZoomWindowButton.CheckOnClick = true;

        _tabBar = new RibbonTabBar(CreateQuickAccess(OpenButton, SaveButton, RecentButton));
        _mainToolbar = new RibbonCommandBar();
        _mainToolbar.AddGroups(
            ("Analysis Elements",
            [
                CreateCommand(
                    "Area",
                    "Area.svg",
                    "Define a layout area. This command will be added here."),
                StraightRouteButton,
                CreateCommand(
                    "Curved Route",
                    "Curved Route.svg",
                    "Draw a curved route. This command will be added here."),
                CreateCommand(
                    "Destination",
                    "Destination.svg",
                    "Place a destination. This command will be added here."),
                CreateCommand(
                    "Delete",
                    "Delete.svg",
                    "Delete layout elements. This command will be added here."),
            ]),
            ("Modify Elements",
            [
                CreateCommand(
                    "Break Routes",
                    "Break Routes.svg",
                    "Break a route into parts. This command will be added here."),
                CreateCommand(
                    "Merge Routes",
                    "Merge Routes.svg",
                    "Merge routes together. This command will be added here."),
                MoveButton,
                RotateButton,
                CreateCommand(
                    "Mirror",
                    "Mirror.svg",
                    "Mirror layout elements. This command will be added here."),
                CreateCommand(
                    "Lenghten Route",
                    "Lenghten Route.svg",
                    "Lengthen a route. This command will be added here."),
                TrimButton,
                FilletButton,
                CreateCommand(
                    "Add Chamfer",
                    "Add Chamfer.svg",
                    "Add a chamfer to a corner. This command will be added here."),
            ]),
            ("Clipboard",
            [
                CopyElementsButton,
                CreateCommand(
                    "Paste",
                    "Paste.svg",
                    "Paste layout elements. This command will be added here.",
                    shortcut: "Ctrl+V"),
                CutButton,
            ]),
            ("View",
            [
                ZoomExtentsButton,
                ZoomWindowButton,
            ]),
            ("Precision Tools",
            [
                GridButton,
                NodeSnapButton,
                GeometricSnapButton,
                OrthogonalSnapButton,
                CreateCommand(
                    "Angle Snap",
                    "Angle Snap.svg",
                    "Snap to angles. This command will be added here."),
            ]),
            ("Other Tools",
            [
                CreateCommand(
                    "Add Dimension",
                    "Add Dimension.svg",
                    "Add a dimension. This command will be added here."),
                CreateCommand(
                    "Measure",
                    "Measure.svg",
                    "Measure a distance. This command will be added here."),
                CreateCommand(
                    "Properties",
                    "Properties.svg",
                    "Show element properties. This command will be added here."),
                CreateCommand(
                    "Add From Library",
                    "Add From Library.svg",
                    "Add an item from the library. This command will be added here."),
                CreateCommand(
                    "Search",
                    "Search.svg",
                    "Search the layout. This command will be added here."),
                LayersButton,
                CreateCommand(
                    "Filter",
                    "Filter.svg",
                    "Filter layout items. This command will be added here."),
            ]),
            ("Simulation Control",
            [
                CreateCommand(
                    "Reset",
                    "Reset.svg",
                    "Reset the simulation. This command will be added here."),
                CreateCommand(
                    "Start",
                    "Start.svg",
                    "Start the simulation. This command will be added here."),
                CreateCommand(
                    "Stop",
                    "Stop.svg",
                    "Stop the simulation. This command will be added here."),
                CreateCommand(
                    "Fast Forward",
                    "Fast Forward.svg",
                    "Fast-forward the simulation. This command will be added here."),
                CreateCommand(
                    "Skip",
                    "Skip.svg",
                    "Skip ahead in the simulation. This command will be added here."),
                CreateCommand(
                    "Step",
                    "Step.svg",
                    "Step the simulation forward. This command will be added here."),
            ]));

        _tabBar.Dock = DockStyle.Top;
        _tabBar.Height = 32;
        _tabBar.UndoClicked += (_, _) => UndoClicked?.Invoke(this, EventArgs.Empty);
        _tabBar.RedoClicked += (_, _) => RedoClicked?.Invoke(this, EventArgs.Empty);
        _mainToolbar.Dock = DockStyle.Fill;

        Controls.Add(_mainToolbar);
        Controls.Add(_tabBar);
    }

    public ToolStripButton OpenButton { get; }
    public ToolStripButton SaveButton { get; }
    public ToolStripDropDownButton RecentButton { get; }
    public ToolStripButton LayersButton { get; }
    public ToolStripButton CopyButton { get; }
    public ToolStripButton PasteButton { get; }
    public ToolStripButton ZoomExtentsButton { get; }
    public ToolStripButton GridButton { get; }
    public ToolStripButton ZoomWindowButton { get; }
    public ToolStripButton NodeSnapButton { get; }
    public ToolStripButton GeometricSnapButton { get; }
    public ToolStripButton OrthogonalSnapButton { get; }
    public ToolStripButton StraightRouteButton { get; }
    public ToolStripButton MoveButton { get; }
    public ToolStripButton RotateButton { get; }
    public ToolStripButton CopyElementsButton { get; }
    public ToolStripButton CutButton { get; }
    public ToolStripButton TrimButton { get; }
    public ToolStripButton FilletButton { get; }

    public ToolStripButton? FindCommand(string name) => _mainToolbar.FindCommand(name);

    public event EventHandler? UndoClicked;
    public event EventHandler? RedoClicked;

    public void SetUndoEnabled(bool enabled) => _tabBar.SetUndoEnabled(enabled);

    public void SetRedoEnabled(bool enabled) => _tabBar.SetRedoEnabled(enabled);

    public CaptionRegion HitCaption(Form form, Point formClient)
    {
        Point local = _tabBar.PointToClient(form.PointToScreen(formClient));
        return _tabBar.HitCaption(local);
    }

    public void AttachHost(Form form) => _tabBar.AttachHost(form);

    public void BindCommands(
        EventHandler onOpen,
        EventHandler onSave,
        EventHandler onLayers,
        EventHandler onCopy,
        EventHandler onPaste,
        EventHandler onZoomExtents,
        EventHandler onGridChanged,
        EventHandler onZoomWindow)
    {
        OpenButton.Click += onOpen;
        SaveButton.Click += onSave;
        LayersButton.Click += onLayers;
        CopyButton.Click += onCopy;
        PasteButton.Click += onPaste;
        ZoomExtentsButton.Click += onZoomExtents;
        GridButton.CheckedChanged += onGridChanged;
        ZoomWindowButton.CheckedChanged += onZoomWindow;
    }

    public void SetRecentFiles(IReadOnlyList<string> paths, Action<string> open)
    {
        RecentButton.DropDownItems.Clear();
        string[] existing = paths.Where(File.Exists).ToArray();
        if (existing.Length == 0)
        {
            RecentButton.DropDownItems.Add(new ToolStripMenuItem("No recent files")
            {
                Enabled = false,
                ForeColor = Color.Silver,
            });
            return;
        }
        foreach (string path in existing)
        {
            string captured = path;
            var item = new ToolStripMenuItem(Path.GetFileName(captured))
            {
                ToolTipText = captured,
                ForeColor = Color.Gainsboro,
            };
            item.Click += (_, _) => open(captured);
            RecentButton.DropDownItems.Add(item);
        }
    }

    private static ToolStrip CreateQuickAccess(params ToolStripItem[] items)
    {
        var strip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = TabBarColor,
            ForeColor = Color.WhiteSmoke,
            Renderer = new DarkToolStripRenderer(TabBarColor),
            Padding = new Padding(0, 1, 0, 1),
            AutoSize = true,
            ImageScalingSize = new Size(18, 18),
            ShowItemToolTips = false,
            CanOverflow = false,
            Dock = DockStyle.None,
        };
        if (items.Length > 0)
        {
            strip.Items.AddRange(items);
        }
        _ = CadToolTip.Attach(strip);
        return strip;
    }

    private static ToolStripButton CreateCommand(
        string text,
        string iconFile,
        string description,
        bool compact = false,
        string shortcut = "") =>
        CreateCommand(text, AppIcons.Load(iconFile), description, compact, shortcut);

    private static ToolStripButton CreateCommand(
        string text,
        Image? icon,
        string description,
        bool compact = false,
        string shortcut = "")
    {
        var tip = new CommandTip(text, description, shortcut);
        return new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            Image = icon,
            ImageScaling = ToolStripItemImageScaling.SizeToFit,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            AutoSize = true,
            AutoToolTip = false,
            Tag = tip,
            ToolTipText = $"{text}{Environment.NewLine}{description}",
            Margin = compact ? new Padding(1, 0, 1, 0) : new Padding(3),
            Padding = compact ? new Padding(4, 1, 6, 1) : new Padding(8, 4, 10, 4),
            ForeColor = Color.WhiteSmoke,
        };
    }

    private static ToolStripSeparator CreateSeparator() =>
        new() { Margin = new Padding(6, 0, 6, 0) };

    private ToolStripDropDownButton CreateDropDown(string text, string iconFile, string description)
    {
        var button = new ToolStripDropDownButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            Image = AppIcons.Load(iconFile),
            ImageScaling = ToolStripItemImageScaling.SizeToFit,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            AutoSize = true,
            AutoToolTip = false,
            Tag = new CommandTip(text, description),
            ToolTipText = $"{text}{Environment.NewLine}{description}",
            Margin = new Padding(1, 0, 1, 0),
            Padding = new Padding(4, 1, 6, 1),
            ForeColor = Color.WhiteSmoke,
            ShowDropDownArrow = true,
        };
        var menu = new ToolStripDropDownMenu
        {
            BackColor = CommandBarColor,
            ForeColor = Color.Gainsboro,
            Renderer = new DarkToolStripRenderer(),
            ShowImageMargin = false,
        };
        button.DropDown = menu;
        return button;
    }

    private sealed class RibbonTabBar : Control
    {
        private readonly ToolStrip _commands;
        private readonly Image? _appLogo = AppIcons.LoadInterfaceLogo();
        private readonly Image? _undoIcon = AppIcons.Load("Undo.svg");
        private readonly Image? _redoIcon = AppIcons.Load("Redo.svg");
        private readonly Image? _settingsIcon = AppIcons.Load("Settings.svg");
        private readonly Font _windowGlyphs = new("Segoe MDL2 Assets", 10f);
        private const int WindowButtonWidth = 40;
        private const int TopInset = 2;
        private Rectangle _logoBounds;
        private Rectangle _undoBounds;
        private Rectangle _redoBounds;
        private Rectangle _settingsBounds;
        private Rectangle _minBounds;
        private Rectangle _maxBounds;
        private Rectangle _closeBounds;
        private bool _undoHot;
        private bool _undoPressed;
        private bool _undoEnabled;
        private bool _redoHot;
        private bool _redoPressed;
        private bool _redoEnabled;
        private bool _settingsHot;
        private bool _settingsPressed;
        private int _windowHot = -1;
        private int _windowPressed = -1;
        private bool _restoreDrag;
        private Point _pressLocal;
        private Form? _host;
        private readonly CadToolTip _commandTip;

        public RibbonTabBar(ToolStrip commands)
        {
            ArgumentNullException.ThrowIfNull(commands);
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            BackColor = TabBarColor;
            Font = new Font("Segoe UI", 9f);
            Cursor = Cursors.Default;
            _commands = commands;
            Controls.Add(_commands);
            _commandTip = CadToolTip.Attach(this, HitCommandTip);
        }

        public CaptionRegion HitCaption(Point local)
        {
            LayoutWindowButtons();
            if (!ClientRectangle.Contains(local))
            {
                return CaptionRegion.Client;
            }
            int window = WindowButtonAt(local);
            if (window == 0)
            {
                return CaptionRegion.Minimize;
            }
            if (window == 1)
            {
                return CaptionRegion.Maximize;
            }
            if (window == 2)
            {
                return CaptionRegion.Close;
            }
            if ((!_settingsBounds.IsEmpty && _settingsBounds.Contains(local))
                || (!_undoBounds.IsEmpty && _undoBounds.Contains(local))
                || (!_redoBounds.IsEmpty && _redoBounds.Contains(local))
                || _commands.Bounds.Contains(local))
            {
                return CaptionRegion.Client;
            }
            if (!_logoBounds.IsEmpty && _logoBounds.Contains(local))
            {
                return CaptionRegion.SystemMenu;
            }
            return CaptionRegion.Drag;
        }

        public void AttachHost(Form form) => BindHost(form);

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            BindHost(FindForm() ?? _host);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            BindHost(FindForm() ?? _host);
        }

        private void BindHost(Form? form)
        {
            if (ReferenceEquals(_host, form))
            {
                return;
            }
            if (_host is not null)
            {
                _host.Resize -= OnHostChanged;
            }
            _host = form;
            if (_host is not null)
            {
                _host.Resize += OnHostChanged;
            }
            Invalidate();
        }

        private void OnHostChanged(object? sender, EventArgs e) => Invalidate();

        public event EventHandler? UndoClicked;
        public event EventHandler? RedoClicked;

        public void SetUndoEnabled(bool enabled)
        {
            if (_undoEnabled == enabled)
            {
                return;
            }
            _undoEnabled = enabled;
            if (!enabled)
            {
                _undoHot = false;
                _undoPressed = false;
            }
            Invalidate();
        }

        public void SetRedoEnabled(bool enabled)
        {
            if (_redoEnabled == enabled)
            {
                return;
            }
            _redoEnabled = enabled;
            if (!enabled)
            {
                _redoHot = false;
                _redoPressed = false;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(TabBarColor);
            int contentTop = TopInset;
            int contentHeight = Math.Max(22, Height - TopInset - 1);
            int x = 2;
            if (_appLogo is not null)
            {
                int logoHeight = Math.Max(16, contentHeight - 2);
                int logoWidth = Math.Max(
                    16,
                    (int)Math.Round(logoHeight * (_appLogo.Width / (double)_appLogo.Height)));
                _logoBounds = new Rectangle(
                    2,
                    contentTop + (contentHeight - logoHeight) / 2,
                    logoWidth,
                    logoHeight);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(_appLogo, _logoBounds);
                x = _logoBounds.Right + 6;
            }
            else
            {
                _logoBounds = Rectangle.Empty;
            }
            int chromeSize = Math.Max(22, contentHeight - 6);
            int chromeY = contentTop + (contentHeight - chromeSize) / 2;
            if (_settingsIcon is not null)
            {
                _settingsBounds = new Rectangle(x, chromeY, chromeSize, chromeSize);
                DrawChromeButton(graphics, _settingsBounds, _settingsHot, _settingsPressed, enabled: true);
                DrawChromeIcon(graphics, _settingsIcon, Rectangle.Inflate(_settingsBounds, -4, -4), enabled: true);
                x = _settingsBounds.Right + 4;
            }
            else
            {
                _settingsBounds = Rectangle.Empty;
            }
            _undoBounds = _undoIcon is null
                ? Rectangle.Empty
                : new Rectangle(x, chromeY, chromeSize, chromeSize);
            if (_undoIcon is not null)
            {
                DrawChromeButton(graphics, _undoBounds, _undoHot, _undoPressed, _undoEnabled);
                DrawChromeIcon(graphics, _undoIcon, Rectangle.Inflate(_undoBounds, -4, -4), _undoEnabled);
                x = _undoBounds.Right + 4;
            }
            _redoBounds = _redoIcon is null
                ? Rectangle.Empty
                : new Rectangle(x, chromeY, chromeSize, chromeSize);
            if (_redoIcon is not null)
            {
                DrawChromeButton(graphics, _redoBounds, _redoHot, _redoPressed, _redoEnabled);
                DrawChromeIcon(graphics, _redoIcon, Rectangle.Inflate(_redoBounds, -4, -4), _redoEnabled);
                x = _redoBounds.Right + 4;
            }
            PlaceCommands(x, contentTop, contentHeight);
            LayoutWindowButtons();
            DrawWindowButtons(graphics);
            using var border = new Pen(BorderColor);
            graphics.DrawLine(border, 0, Height - 1, Width, Height - 1);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_restoreDrag)
            {
                TryRestoreDrag(e);
                return;
            }

            LayoutWindowButtons();
            bool overUndo = !_undoBounds.IsEmpty && _undoBounds.Contains(e.Location);
            bool overRedo = !_redoBounds.IsEmpty && _redoBounds.Contains(e.Location);
            bool overSettings = !_settingsBounds.IsEmpty && _settingsBounds.Contains(e.Location);
            int windowHot = WindowButtonAt(e.Location);
            Cursor = overSettings || (overUndo && _undoEnabled) || (overRedo && _redoEnabled)
                ? Cursors.Hand
                : Cursors.Default;
            if (overUndo != _undoHot
                || overRedo != _redoHot
                || overSettings != _settingsHot
                || windowHot != _windowHot)
            {
                _undoHot = overUndo;
                _redoHot = overRedo;
                _settingsHot = overSettings;
                _windowHot = windowHot;
                Invalidate();
            }
        }

        private (CommandTip Tip, Rectangle Anchor)? HitCommandTip(Point local)
        {
            if (_restoreDrag)
            {
                return null;
            }

            LayoutWindowButtons();
            if (!_undoBounds.IsEmpty && _undoBounds.Contains(local))
            {
                return (
                    new CommandTip(
                        "Undo",
                        _undoEnabled ? "Reverse the last action." : "Nothing to undo.",
                        "Ctrl+Z"),
                    _undoBounds);
            }

            if (!_redoBounds.IsEmpty && _redoBounds.Contains(local))
            {
                return (
                    new CommandTip(
                        "Redo",
                        _redoEnabled ? "Repeat the last undone action." : "Nothing to redo.",
                        "Ctrl+Y"),
                    _redoBounds);
            }

            if (!_settingsBounds.IsEmpty && _settingsBounds.Contains(local))
            {
                return (
                    new CommandTip("Settings", "Application settings will be added here."),
                    _settingsBounds);
            }

            if (!_logoBounds.IsEmpty && _logoBounds.Contains(local))
            {
                return (new CommandTip(ProductInfo.Name, "Application menu."), _logoBounds);
            }

            return WindowButtonAt(local) switch
            {
                0 => (new CommandTip("Minimize", "Minimize the window."), _minBounds),
                1 => (
                    new CommandTip(
                        _host?.WindowState == FormWindowState.Maximized ? "Restore Down" : "Maximize",
                        _host?.WindowState == FormWindowState.Maximized
                            ? "Restore the window to its previous size."
                            : "Maximize the window."),
                    _maxBounds),
                2 => (new CommandTip("Close", "Close IntraLayout Studio."), _closeBounds),
                _ => null,
            };
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (Capture)
            {
                return;
            }
            Cursor = Cursors.Default;
            if (!_undoHot && !_undoPressed
                && !_redoHot && !_redoPressed
                && !_settingsHot && !_settingsPressed
                && _windowHot < 0 && _windowPressed < 0)
            {
                return;
            }
            _undoHot = false;
            _undoPressed = false;
            _redoHot = false;
            _redoPressed = false;
            _settingsHot = false;
            _settingsPressed = false;
            _windowHot = -1;
            _windowPressed = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            if (_undoEnabled && !_undoBounds.IsEmpty && _undoBounds.Contains(e.Location))
            {
                _undoPressed = true;
                Capture = true;
                Invalidate();
                return;
            }
            if (_redoEnabled && !_redoBounds.IsEmpty && _redoBounds.Contains(e.Location))
            {
                _redoPressed = true;
                Capture = true;
                Invalidate();
                return;
            }
            if (!_settingsBounds.IsEmpty && _settingsBounds.Contains(e.Location))
            {
                _settingsPressed = true;
                Capture = true;
                Invalidate();
                return;
            }
            LayoutWindowButtons();
            int window = WindowButtonAt(e.Location);
            if (window >= 0)
            {
                _windowPressed = window;
                Capture = true;
                Invalidate();
                return;
            }

            Form? host = _host ?? FindForm();
            if (host is null || HitCaption(e.Location) != CaptionRegion.Drag)
            {
                return;
            }

            if (host.WindowState == FormWindowState.Maximized)
            {
                _restoreDrag = true;
                _pressLocal = e.Location;
                Capture = true;
                return;
            }

            CaptionFrame.BeginNativeDrag(host);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_undoPressed)
            {
                bool fire = _undoEnabled && !_undoBounds.IsEmpty && _undoBounds.Contains(e.Location);
                _undoPressed = false;
                Capture = false;
                Invalidate();
                if (fire)
                {
                    UndoClicked?.Invoke(this, EventArgs.Empty);
                }
                return;
            }
            if (_redoPressed)
            {
                bool fireRedo = _redoEnabled && !_redoBounds.IsEmpty && _redoBounds.Contains(e.Location);
                _redoPressed = false;
                Capture = false;
                Invalidate();
                if (fireRedo)
                {
                    RedoClicked?.Invoke(this, EventArgs.Empty);
                }
                return;
            }
            if (_settingsPressed)
            {
                _settingsPressed = false;
                Capture = false;
                Invalidate();
                return;
            }
            if (_windowPressed < 0)
            {
                _restoreDrag = false;
                Capture = false;
                return;
            }
            int pressed = _windowPressed;
            _windowPressed = -1;
            Capture = false;
            Invalidate();
            Form? host = _host ?? FindForm();
            if (WindowButtonAt(e.Location) != pressed || host is null)
            {
                return;
            }
            switch (pressed)
            {
                case 0:
                    host.WindowState = FormWindowState.Minimized;
                    break;
                case 1:
                    host.WindowState = host.WindowState == FormWindowState.Maximized
                        ? FormWindowState.Normal
                        : FormWindowState.Maximized;
                    break;
                case 2:
                    host.Close();
                    break;
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || HitCaption(e.Location) != CaptionRegion.Drag)
            {
                return;
            }

            Form? host = _host ?? FindForm();
            if (host is null)
            {
                return;
            }

            host.WindowState = host.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        }

        private void TryRestoreDrag(MouseEventArgs e)
        {
            Form? host = _host ?? FindForm();
            if (host is null || e.Button != MouseButtons.Left)
            {
                _restoreDrag = false;
                Capture = false;
                return;
            }

            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(e.X - _pressLocal.X) <= threshold.Width
                && Math.Abs(e.Y - _pressLocal.Y) <= threshold.Height)
            {
                return;
            }

            _restoreDrag = false;
            Capture = false;
            CaptionFrame.RestoreAndDrag(host, PointToScreen(e.Location), e.Y);
        }

        private void PlaceCommands(int x, int contentTop, int contentHeight)
        {
            Size preferred = _commands.GetPreferredSize(Size);
            int height = Math.Clamp(preferred.Height, 24, Math.Max(24, contentHeight));
            var location = new Point(x, contentTop + Math.Max(0, (contentHeight - height) / 2));
            if (_commands.Location != location)
            {
                _commands.Location = location;
            }
            if (_commands.Height != height)
            {
                _commands.Height = height;
            }
        }

        private void LayoutWindowButtons()
        {
            _closeBounds = new Rectangle(Width - WindowButtonWidth, 0, WindowButtonWidth, Height);
            _maxBounds = new Rectangle(_closeBounds.Left - WindowButtonWidth, 0, WindowButtonWidth, Height);
            _minBounds = new Rectangle(_maxBounds.Left - WindowButtonWidth, 0, WindowButtonWidth, Height);
        }

        private void DrawWindowButtons(Graphics graphics)
        {
            bool maximized = (_host ?? FindForm())?.WindowState == FormWindowState.Maximized;
            DrawWindowGlyph(_minBounds, 0, "\uE921", isClose: false);
            DrawWindowGlyph(_maxBounds, 1, maximized ? "\uE923" : "\uE922", isClose: false);
            DrawWindowGlyph(_closeBounds, 2, "\uE8BB", isClose: true);
            return;

            void DrawWindowGlyph(Rectangle bounds, int index, string glyph, bool isClose)
            {
                bool hot = _windowHot == index || _windowPressed == index;
                if (hot)
                {
                    Color fillColor = isClose
                        ? Color.FromArgb(232, 17, 35)
                        : _windowPressed == index
                            ? Color.FromArgb(70, 74, 82)
                            : Color.FromArgb(58, 60, 68);
                    using var fill = new SolidBrush(fillColor);
                    graphics.FillRectangle(fill, bounds);
                }

                TextRenderer.DrawText(
                    graphics,
                    glyph,
                    _windowGlyphs,
                    bounds,
                    isClose && hot ? Color.White : Color.FromArgb(220, 222, 226),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private int WindowButtonAt(Point point)
        {
            if (_minBounds.Contains(point))
            {
                return 0;
            }
            if (_maxBounds.Contains(point))
            {
                return 1;
            }
            if (_closeBounds.Contains(point))
            {
                return 2;
            }
            return -1;
        }

        private static void DrawChromeButton(
            Graphics graphics,
            Rectangle bounds,
            bool hot,
            bool pressed,
            bool enabled)
        {
            if (!enabled || (!hot && !pressed))
            {
                return;
            }
            Color fillColor = pressed
                ? Color.FromArgb(70, 74, 82)
                : Color.FromArgb(52, 54, 61);
            using var fill = new SolidBrush(fillColor);
            graphics.FillRectangle(fill, bounds);
        }

        private static void DrawChromeIcon(
            Graphics graphics,
            Image icon,
            Rectangle dest,
            bool enabled)
        {
            if (enabled)
            {
                graphics.DrawImage(icon, dest);
                return;
            }

            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            var matrix = new System.Drawing.Imaging.ColorMatrix
            {
                Matrix33 = 0.35f,
            };
            attributes.SetColorMatrix(matrix);
            graphics.DrawImage(
                icon,
                dest,
                0,
                0,
                icon.Width,
                icon.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                BindHost(null);
                _appLogo?.Dispose();
                _undoIcon?.Dispose();
                _redoIcon?.Dispose();
                _settingsIcon?.Dispose();
                _windowGlyphs.Dispose();
                _commandTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer()
            : this(CommandBarColor)
        {
        }

        public DarkToolStripRenderer(Color stripColor)
            : base(new DarkColorTable(stripColor))
        {
        }
    }

    private sealed class DarkColorTable(Color strip) : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => strip;
        public override Color ToolStripGradientMiddle => strip;
        public override Color ToolStripGradientEnd => strip;
        public override Color ButtonSelectedHighlight => Color.FromArgb(65, 72, 80);
        public override Color ButtonSelectedGradientBegin => Color.FromArgb(65, 72, 80);
        public override Color ButtonSelectedGradientEnd => Color.FromArgb(65, 72, 80);
        public override Color ButtonCheckedGradientBegin => Color.FromArgb(0, 120, 215);
        public override Color ButtonCheckedGradientEnd => Color.FromArgb(0, 100, 190);
        public override Color SeparatorDark => Color.FromArgb(75, 78, 82);
        public override Color SeparatorLight => Color.FromArgb(75, 78, 82);
        public override Color ToolStripBorder => strip;
        public override Color ImageMarginGradientBegin => strip;
        public override Color ImageMarginGradientMiddle => strip;
        public override Color ImageMarginGradientEnd => strip;
        public override Color OverflowButtonGradientBegin => strip;
        public override Color OverflowButtonGradientMiddle => strip;
        public override Color OverflowButtonGradientEnd => strip;
    }
}
