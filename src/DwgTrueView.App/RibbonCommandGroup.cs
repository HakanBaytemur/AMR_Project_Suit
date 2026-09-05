namespace DwgTrueView.App;

/// <summary>
/// One ribbon command group: full icon row when there is room, AutoCAD-style
/// collapsed tile (icon, name, chevron) when the bar is too narrow.
/// </summary>
internal sealed class RibbonCommandGroup : Panel
{
    internal static readonly Color BarColor = Color.FromArgb(0x3B, 0x44, 0x53);
    internal static readonly Color BorderColor = Color.FromArgb(0x22, 0x29, 0x33);
    internal static readonly Color TileColor = Color.FromArgb(0x4E, 0x5A, 0x6E);
    internal static readonly Color TileHotColor = Color.FromArgb(0x22, 0x29, 0x33);
    internal static readonly Color LabelColor = Color.FromArgb(220, 222, 228);

    private const int CollapsedMinWidth = 68;
    private const int CollapsedGap = 2;
    private const int ExpandedDividerWidth = 1;
    private const int GroupHeight = 68;

    private readonly ToolStripItem[] _items;
    private readonly ToolStrip _strip;
    private readonly Label _label;
    private readonly Panel? _divider;
    private readonly ToolStripDropDownMenu _popup;
    private readonly CadToolTip _popupTips;
    private readonly System.Windows.Forms.Timer _hoverClose;
    private readonly int _expandedWidth;
    private readonly int _collapsedBodyWidth;
    private readonly Image? _icon;
    private bool _collapsed;
    private bool _hot;
    private bool _open;

    public RibbonCommandGroup(string title, ToolStripItem[] items, bool showDivider)
    {
        Title = title;
        _items = items;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Height = GroupHeight;
        BackColor = BarColor;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Cursor = Cursors.Default;

        _strip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = BarColor,
            ForeColor = Color.WhiteSmoke,
            Renderer = new DarkRenderer(),
            Padding = new Padding(4, 4, 4, 0),
            AutoSize = true,
            Dock = DockStyle.None,
            ImageScalingSize = new Size(24, 24),
            ShowItemToolTips = false,
            CanOverflow = false,
        };
        if (items.Length > 0)
        {
            _strip.Items.AddRange(items);
        }

        foreach (ToolStripItem item in _strip.Items)
        {
            if (item is not ToolStripButton button)
            {
                continue;
            }

            button.DisplayStyle = ToolStripItemDisplayStyle.Image;
            button.AutoToolTip = false;
            button.Margin = new Padding(1, 2, 1, 2);
            button.Padding = new Padding(4);
        }

        _ = CadToolTip.Attach(_strip);
        _icon = items.Select(item => item.Image).FirstOrDefault(image => image is not null);

        var labelFont = new Font("Segoe UI", 9f);
        int labelWidth = TextRenderer.MeasureText(title, labelFont).Width + 16;
        int stripWidth = _strip.GetPreferredSize(Size.Empty).Width;
        _expandedWidth = Math.Max(labelWidth, stripWidth) + (showDivider ? 7 : 4);
        _collapsedBodyWidth = Math.Max(
            CollapsedMinWidth,
            MeasureWrappedTitleWidth(title, labelFont) + 10);

        _label = new Label
        {
            Text = title,
            Dock = DockStyle.Bottom,
            Height = 18,
            TextAlign = ContentAlignment.TopCenter,
            ForeColor = LabelColor,
            Font = labelFont,
            BackColor = BarColor,
        };
        _strip.Location = new Point(2, 1);
        _strip.Width = _expandedWidth - (showDivider ? 8 : 4);
        _strip.Height = 46;
        Controls.Add(_strip);
        Controls.Add(_label);
        if (showDivider)
        {
            _divider = new Panel
            {
                Dock = DockStyle.Right,
                Width = ExpandedDividerWidth,
                BackColor = BorderColor,
                Margin = Padding.Empty,
            };
            Controls.Add(_divider);
        }

        _hoverClose = new System.Windows.Forms.Timer { Interval = 220 };
        _hoverClose.Tick += (_, _) => ClosePopupIfPointerLeft();

        _popup = new ToolStripDropDownMenu
        {
            BackColor = BarColor,
            ForeColor = Color.WhiteSmoke,
            Renderer = new DarkRenderer(),
            ShowImageMargin = true,
            AutoClose = true,
        };
        _popup.Opened += (_, _) => _hoverClose.Stop();
        _popup.MouseEnter += (_, _) => _hoverClose.Stop();
        _popup.MouseLeave += (_, _) =>
        {
            if (!PointerOverFlyout())
            {
                _hoverClose.Start();
            }
        };
        _popupTips = CadToolTip.Attach(_popup);
        _popup.Closing += OnPopupClosing;
        _popup.Closed += (_, _) =>
        {
            _hoverClose.Stop();
            _popupTips.Hide();
            _open = false;
            _hot = PointerOverGroup();
            Invalidate();
        };

        Width = _expandedWidth;
    }

    public string Title { get; }

    public int ExpandedWidth => _expandedWidth;

    public int CollapsedWidth => _collapsedBodyWidth + CollapsedGap;

    public bool IsCollapsed => _collapsed;

    public ToolStripButton? FindCommand(string name) =>
        _items.OfType<ToolStripButton>()
            .FirstOrDefault(item => string.Equals(item.Text, name, StringComparison.OrdinalIgnoreCase));

    public void SetCollapsed(bool collapsed)
    {
        if (_collapsed == collapsed)
        {
            return;
        }

        if (!collapsed)
        {
            ClosePopup();
        }

        _collapsed = collapsed;
        _strip.Visible = !collapsed;
        _label.Visible = !collapsed;
        if (_divider is not null)
        {
            _divider.Visible = !collapsed;
        }

        Margin = collapsed ? new Padding(0, 0, CollapsedGap, 0) : Padding.Empty;
        Width = collapsed ? _collapsedBodyWidth : _expandedWidth;
        BackColor = collapsed ? TileColor : BarColor;
        _hot = false;
        Invalidate();
    }

    public void ClosePopup()
    {
        _hoverClose.Stop();
        if (_popup.Visible)
        {
            _popup.Close();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (!_collapsed)
        {
            return;
        }

        _hoverClose.Stop();
        _hot = true;
        Invalidate();
        OpenPopup();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_collapsed)
        {
            return;
        }

        if (!PointerOverFlyout())
        {
            _hoverClose.Start();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_collapsed)
        {
            return;
        }

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        int bodyWidth = ClientSize.Width;
        if (_icon is not null)
        {
            const int icon = 24;
            var dest = new Rectangle((bodyWidth - icon) / 2, 3, icon, icon);
            graphics.DrawImage(_icon, dest);
        }

        TextRenderer.DrawText(
            graphics,
            Title,
            _label.Font,
            new Rectangle(3, 28, bodyWidth - 6, 28),
            LabelColor,
            TextFormatFlags.HorizontalCenter
            | TextFormatFlags.WordBreak
            | TextFormatFlags.TextBoxControl
            | TextFormatFlags.NoPadding);

        int cx = bodyWidth / 2;
        int cy = Height - 10;
        using var pen = new Pen(_hot || _open ? Color.White : Color.FromArgb(180, 182, 188), 1.4f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        graphics.DrawLines(
            pen,
            new Point[] { new(cx - 4, cy - 1), new(cx, cy + 3), new(cx + 4, cy - 1) });
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Color fill = !_collapsed
            ? BarColor
            : _hot || _open ? TileHotColor : TileColor;
        e.Graphics.Clear(fill);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClosePopup();
            _hoverClose.Dispose();
            _popupTips.Dispose();
            _popup.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OpenPopup()
    {
        if (_popup.Visible)
        {
            return;
        }

        _popup.Items.Clear();
        foreach (ToolStripItem item in _items)
        {
            if (item is ToolStripSeparator)
            {
                _popup.Items.Add(new ToolStripSeparator());
                continue;
            }

            if (item is not ToolStripButton button)
            {
                continue;
            }

            var menuItem = new ToolStripMenuItem(button.Text, button.Image)
            {
                Tag = button.Tag,
                Checked = button.Checked,
                ForeColor = Color.WhiteSmoke,
                ImageScaling = ToolStripItemImageScaling.SizeToFit,
                AutoToolTip = false,
            };
            ToolStripButton captured = button;
            menuItem.Click += (_, _) => captured.PerformClick();
            _popup.Items.Add(menuItem);
        }

        _popup.Items.Add(new ToolStripSeparator());
        _popup.Items.Add(new ToolStripLabel(Title)
        {
            ForeColor = Color.FromArgb(170, 174, 180),
            Enabled = false,
        });

        _open = true;
        _hot = true;
        Invalidate();
        _popup.Show(this, new Point(0, Height));
    }

    private static int MeasureWrappedTitleWidth(string title, Font font)
    {
        string[] words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
        {
            return MeasureLine(title, font);
        }

        int best = int.MaxValue;
        for (int split = 1; split < words.Length; split++)
        {
            int top = MeasureLine(string.Join(' ', words.Take(split)), font);
            int bottom = MeasureLine(string.Join(' ', words.Skip(split)), font);
            best = Math.Min(best, Math.Max(top, bottom));
        }

        return best;
    }

    private static int MeasureLine(string text, Font font) =>
        TextRenderer.MeasureText(
            text,
            font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;

    private void OnPopupClosing(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        if (e.CloseReason is ToolStripDropDownCloseReason.CloseCalled
            or ToolStripDropDownCloseReason.Keyboard)
        {
            return;
        }

        if (PointerOverFlyout())
        {
            e.Cancel = true;
        }
    }

    private void ClosePopupIfPointerLeft()
    {
        if (PointerOverGroup() || PointerOverPopup())
        {
            _hoverClose.Stop();
            return;
        }

        if (CadToolTip.IsPointerOverAnyTip())
        {
            return;
        }

        _hoverClose.Stop();
        ClosePopup();
        _hot = false;
        Invalidate();
    }

    private bool PointerOverFlyout() =>
        PointerOverGroup() || PointerOverPopup() || CadToolTip.IsPointerOverAnyTip();

    private bool PointerOverGroup() =>
        IsHandleCreated && RectangleToScreen(ClientRectangle).Contains(MousePosition);

    private bool PointerOverPopup() =>
        _popup.Visible && _popup.Bounds.Contains(MousePosition);

    internal sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer()
            : base(new DarkTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
        }
    }

    private sealed class DarkTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => BarColor;
        public override Color ToolStripGradientMiddle => BarColor;
        public override Color ToolStripGradientEnd => BarColor;
        public override Color ButtonSelectedHighlight => Color.FromArgb(65, 72, 80);
        public override Color ButtonSelectedGradientBegin => Color.FromArgb(65, 72, 80);
        public override Color ButtonSelectedGradientEnd => Color.FromArgb(65, 72, 80);
        public override Color ButtonCheckedGradientBegin => Color.FromArgb(0, 120, 215);
        public override Color ButtonCheckedGradientEnd => Color.FromArgb(0, 100, 190);
        public override Color SeparatorDark => BorderColor;
        public override Color SeparatorLight => BorderColor;
        public override Color ToolStripBorder => BarColor;
        public override Color ImageMarginGradientBegin => BarColor;
        public override Color ImageMarginGradientMiddle => BarColor;
        public override Color ImageMarginGradientEnd => BarColor;
        public override Color MenuItemSelected => Color.FromArgb(65, 72, 80);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(65, 72, 80);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(65, 72, 80);
        public override Color MenuItemBorder => Color.FromArgb(65, 72, 80);
        public override Color MenuBorder => BorderColor;
        public override Color ToolStripDropDownBackground => BarColor;
    }
}
