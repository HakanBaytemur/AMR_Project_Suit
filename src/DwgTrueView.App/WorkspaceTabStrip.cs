using System.Drawing.Drawing2D;
using DwgTrueView.Core;

namespace DwgTrueView.App;

/// <summary>
/// Chrome / Arc-style tab strip: rounded tabs, hover motion, CAD icons,
/// overflow scrolling, and drag-to-reorder.
/// </summary>
internal sealed class WorkspaceTabStrip : Panel
{
    private const int MinTabWidth = 108;
    private const int MaxTabWidth = 228;
    private const int TabGap = 5;
    private const int TabHeight = 26;
    private const int PlusSize = 22;
    private const int ScrollButton = 18;
    private const int CornerRadius = 8;
    private static readonly Color StripColor = Color.FromArgb(0x22, 0x29, 0x33);
    private static readonly Color InactiveColor = Color.FromArgb(36, 38, 42);
    private static readonly Color HoverColor = Color.FromArgb(52, 56, 62);
    private static readonly Color ActiveColor = Color.FromArgb(42, 45, 49);
    private static readonly Color AccentColor = Color.FromArgb(0, 120, 215);

    private readonly List<TabVisual> _tabs = [];
    private readonly System.Windows.Forms.Timer _animator = new() { Interval = 16 };
    private float[] _hover = [];
    private int _hotIndex = -1;
    private int _closeHotIndex = -1;
    private int _pressedIndex = -1;
    private int _dropIndex = -1;
    private int _scroll;
    private bool _dragging;
    private bool _plusHot;
    private bool _leftHot;
    private bool _rightHot;
    private Point _pressPoint;

    public WorkspaceTabStrip()
    {
        Height = 32;
        BackColor = StripColor;
        Padding = new Padding(8, 3, 8, 0);
        AllowDrop = true;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw,
            true);
        _animator.Tick += (_, _) => TickHover();
        _animator.Start();
        _ = CadToolTip.Attach(this, HitCommandTip);
    }

    public event Action<Guid>? TabSelected;
    public event Action<Guid>? TabClosed;
    public event Action<Guid, int>? TabMoved;
    public event EventHandler? NewTabClicked;

    public void Bind(IReadOnlyList<DrawingWorkspace> tabs, Guid? activeId)
    {
        if (_dragging)
        {
            for (int index = 0; index < _tabs.Count; index++)
            {
                TabVisual tab = _tabs[index];
                _tabs[index] = tab with { IsActive = tab.Id == activeId };
            }
            Invalidate();
            return;
        }

        bool sameOrder = _tabs.Count == tabs.Count;
        if (sameOrder)
        {
            for (int index = 0; index < tabs.Count; index++)
            {
                if (_tabs[index].Id != tabs[index].Id)
                {
                    sameOrder = false;
                    break;
                }
            }
        }

        if (sameOrder)
        {
            for (int index = 0; index < tabs.Count; index++)
            {
                DrawingWorkspace source = tabs[index];
                _tabs[index] = new TabVisual(
                    source.Id,
                    source.FileName,
                    IsDxf(source.SourcePath),
                    source.Id == activeId);
            }
            EnsureActiveVisible();
            Invalidate();
            return;
        }

        _tabs.Clear();
        foreach (DrawingWorkspace tab in tabs)
        {
            _tabs.Add(new TabVisual(
                tab.Id,
                tab.FileName,
                IsDxf(tab.SourcePath),
                tab.Id == activeId));
        }
        Array.Resize(ref _hover, _tabs.Count);
        Array.Clear(_hover);
        _hotIndex = -1;
        _closeHotIndex = -1;
        EnsureActiveVisible();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animator.Stop();
            _animator.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(StripColor);

        LayoutMetrics layout = MeasureLayout();
        GraphicsState state = graphics.Save();
        if (_tabs.Count > 0)
        {
            graphics.SetClip(layout.TabsClip);
            for (int index = 0; index < _tabs.Count; index++)
            {
                if (_dragging && index == _pressedIndex)
                {
                    continue;
                }
                PaintTab(graphics, index, TabBounds(index, layout));
            }
            if (_dragging && (uint)_pressedIndex < (uint)_tabs.Count)
            {
                Rectangle ghost = TabBounds(_pressedIndex, layout);
                ghost.X = Math.Clamp(
                    PointToClient(MousePosition).X - ghost.Width / 2,
                    layout.TabsClip.Left,
                    Math.Max(layout.TabsClip.Left, layout.TabsClip.Right - ghost.Width));
                PaintTab(graphics, _pressedIndex, ghost, dragging: true);
            }
        }
        graphics.Restore(state);

        if (layout.Overflow)
        {
            PaintChevron(graphics, layout.LeftButton, _leftHot, left: true, _scroll > 0);
            PaintChevron(
                graphics,
                layout.RightButton,
                _rightHot,
                left: false,
                _scroll < layout.MaxScroll);
        }
        PaintPlus(graphics, layout.PlusBounds, _plusHot);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        LayoutMetrics layout = MeasureLayout();
        if (_pressedIndex >= 0 && !_dragging
            && (Math.Abs(e.X - _pressPoint.X) > 6 || Math.Abs(e.Y - _pressPoint.Y) > 6))
        {
            _dragging = true;
            Cursor = Cursors.SizeWE;
        }
        if (_dragging)
        {
            _dropIndex = IndexFromX(e.X, layout);
            Invalidate();
            return;
        }

        int hot = HitTab(e.Location, layout, out bool close);
        bool plus = layout.PlusBounds.Contains(e.Location);
        bool left = layout.Overflow && layout.LeftButton.Contains(e.Location);
        bool right = layout.Overflow && layout.RightButton.Contains(e.Location);
        if (hot != _hotIndex
            || (close ? hot : -1) != _closeHotIndex
            || plus != _plusHot
            || left != _leftHot
            || right != _rightHot)
        {
            _hotIndex = hot;
            _closeHotIndex = close ? hot : -1;
            _plusHot = plus;
            _leftHot = left;
            _rightHot = right;
            Invalidate();
        }
        Cursor = plus || left || right || hot >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hotIndex = -1;
        _closeHotIndex = -1;
        _plusHot = false;
        _leftHot = false;
        _rightHot = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        LayoutMetrics layout = MeasureLayout();
        if (e.Button == MouseButtons.Left && layout.Overflow)
        {
            if (layout.LeftButton.Contains(e.Location))
            {
                ScrollBy(-layout.TabWidth);
                return;
            }
            if (layout.RightButton.Contains(e.Location))
            {
                ScrollBy(layout.TabWidth);
                return;
            }
        }
        if (e.Button == MouseButtons.Left && layout.PlusBounds.Contains(e.Location))
        {
            NewTabClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        int index = HitTab(e.Location, layout, out bool close);
        if (index < 0)
        {
            return;
        }
        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && close))
        {
            TabClosed?.Invoke(_tabs[index].Id);
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            _pressedIndex = index;
            _pressPoint = e.Location;
            Capture = true;
            TabSelected?.Invoke(_tabs[index].Id);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging && (uint)_pressedIndex < (uint)_tabs.Count)
        {
            int drop = _dropIndex < 0 ? _pressedIndex : _dropIndex;
            Guid id = _tabs[_pressedIndex].Id;
            _dragging = false;
            _pressedIndex = -1;
            Capture = false;
            Cursor = Cursors.Default;
            TabMoved?.Invoke(id, drop);
            return;
        }
        _dragging = false;
        _pressedIndex = -1;
        Capture = false;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollBy(-Math.Sign(e.Delta) * Math.Max(40, MeasureLayout().TabWidth / 2));
    }

    private void TickHover()
    {
        if (_hover.Length != _tabs.Count)
        {
            Array.Resize(ref _hover, _tabs.Count);
        }
        bool dirty = false;
        for (int index = 0; index < _hover.Length; index++)
        {
            float target = index == _hotIndex || index == _pressedIndex ? 1f : 0f;
            float next = _hover[index] + (target - _hover[index]) * 0.28f;
            if (next < 0.004f)
            {
                next = 0f;
            }
            else if (next > 0.996f)
            {
                next = 1f;
            }
            if (Math.Abs(next - _hover[index]) > 0.002f)
            {
                _hover[index] = next;
                dirty = true;
            }
        }
        if (dirty)
        {
            Invalidate();
        }
    }

    private LayoutMetrics MeasureLayout()
    {
        int plusLeft = Width - PlusSize - 10;
        Rectangle plus = new(plusLeft, Height - TabHeight + (TabHeight - PlusSize) / 2, PlusSize, PlusSize);
        int tabsLeft = 8;
        int tabsRight = plusLeft - 8;
        int count = Math.Max(1, _tabs.Count);
        int natural = count * MaxTabWidth + Math.Max(0, count - 1) * TabGap;
        int available = Math.Max(40, tabsRight - tabsLeft);
        bool overflow = _tabs.Count > 0 && natural > available;
        Rectangle left = Rectangle.Empty;
        Rectangle right = Rectangle.Empty;
        if (overflow)
        {
            left = new Rectangle(tabsLeft, Height - TabHeight + 4, ScrollButton, TabHeight - 6);
            right = new Rectangle(tabsRight - ScrollButton, Height - TabHeight + 4, ScrollButton, TabHeight - 6);
            tabsLeft = left.Right + 4;
            tabsRight = right.Left - 4;
            available = Math.Max(40, tabsRight - tabsLeft);
        }

        int tabWidth = MaxTabWidth;
        if (_tabs.Count > 0)
        {
            int fit = (available - Math.Max(0, _tabs.Count - 1) * TabGap) / _tabs.Count;
            tabWidth = Math.Clamp(fit, MinTabWidth, MaxTabWidth);
        }
        int content = _tabs.Count == 0
            ? 0
            : _tabs.Count * tabWidth + (_tabs.Count - 1) * TabGap;
        overflow = content > available;
        int maxScroll = Math.Max(0, content - available);
        _scroll = Math.Clamp(_scroll, 0, maxScroll);
        Rectangle clip = new(tabsLeft, Height - TabHeight, Math.Max(0, tabsRight - tabsLeft), TabHeight);
        return new LayoutMetrics(tabWidth, clip, plus, left, right, overflow, maxScroll);
    }

    private (CommandTip Tip, Rectangle Anchor)? HitCommandTip(Point local)
    {
        if (_dragging)
        {
            return null;
        }

        LayoutMetrics layout = MeasureLayout();
        if (layout.PlusBounds.Contains(local))
        {
            return (
                new CommandTip("Open File", "Open a DWG or DXF drawing in a new tab.", "Ctrl+O"),
                layout.PlusBounds);
        }

        if (layout.Overflow && layout.LeftButton.Contains(local))
        {
            return (new CommandTip("Scroll Left", "Show tabs hidden on the left."), layout.LeftButton);
        }

        if (layout.Overflow && layout.RightButton.Contains(local))
        {
            return (new CommandTip("Scroll Right", "Show tabs hidden on the right."), layout.RightButton);
        }

        int index = HitTab(local, layout, out bool close);
        if (index < 0)
        {
            return null;
        }

        Rectangle tab = TabBounds(index, layout);
        if (close)
        {
            return (new CommandTip("Close Tab", "Close this drawing tab.", "Ctrl+W"), CloseBounds(tab));
        }

        return (new CommandTip(_tabs[index].Title, "Activate this drawing tab."), tab);
    }

    private Rectangle TabBounds(int index, LayoutMetrics layout)
    {
        int x = layout.TabsClip.X + index * (layout.TabWidth + TabGap) - _scroll;
        return new Rectangle(x, Height - TabHeight, layout.TabWidth, TabHeight);
    }

    private int HitTab(Point point, LayoutMetrics layout, out bool close)
    {
        close = false;
        if (!layout.TabsClip.Contains(point))
        {
            return -1;
        }
        for (int index = 0; index < _tabs.Count; index++)
        {
            Rectangle bounds = TabBounds(index, layout);
            if (!bounds.Contains(point))
            {
                continue;
            }
            close = CloseBounds(bounds).Contains(point)
                && (_tabs[index].IsActive || index == _hotIndex || Hover(index) > 0.15f);
            return index;
        }
        return -1;
    }

    private int IndexFromX(int x, LayoutMetrics layout)
    {
        if (_tabs.Count == 0)
        {
            return 0;
        }
        int local = x - layout.TabsClip.X + _scroll;
        int stride = layout.TabWidth + TabGap;
        return Math.Clamp((local + layout.TabWidth / 2) / Math.Max(1, stride), 0, _tabs.Count - 1);
    }

    private void ScrollBy(int delta)
    {
        LayoutMetrics layout = MeasureLayout();
        int next = Math.Clamp(_scroll + delta, 0, layout.MaxScroll);
        if (next == _scroll)
        {
            return;
        }
        _scroll = next;
        Invalidate();
    }

    private void EnsureActiveVisible()
    {
        int active = _tabs.FindIndex(tab => tab.IsActive);
        if (active < 0)
        {
            return;
        }
        LayoutMetrics layout = MeasureLayout();
        Rectangle bounds = TabBounds(active, layout);
        if (bounds.Left < layout.TabsClip.Left)
        {
            _scroll -= layout.TabsClip.Left - bounds.Left;
        }
        else if (bounds.Right > layout.TabsClip.Right)
        {
            _scroll += bounds.Right - layout.TabsClip.Right;
        }
        _scroll = Math.Clamp(_scroll, 0, layout.MaxScroll);
    }

    private void PaintTab(
        Graphics graphics,
        int index,
        Rectangle bounds,
        bool dragging = false)
    {
        TabVisual tab = _tabs[index];
        float hover = dragging ? 1f : Hover(index);
        Color fill = tab.IsActive
            ? ActiveColor
            : Lerp(InactiveColor, HoverColor, hover);
        using var path = CreateTabPath(bounds, CornerRadius);
        using (var brush = new SolidBrush(Color.FromArgb(dragging ? 210 : 255, fill)))
        {
            graphics.FillPath(brush, path);
        }
        if (tab.IsActive)
        {
            using var accent = new SolidBrush(AccentColor);
            graphics.FillRectangle(accent, bounds.X + 10, bounds.Y, bounds.Width - 20, 2);
        }

        Rectangle icon = new(bounds.X + 10, bounds.Y + (bounds.Height - 14) / 2, 14, 14);
        PaintCadIcon(graphics, icon, tab.IsDxf);

        bool showClose = tab.IsActive || hover > 0.28f || dragging;
        Rectangle close = CloseBounds(bounds);
        int textRight = showClose ? close.Left - 4 : bounds.Right - 10;
        TextRenderer.DrawText(
            graphics,
            tab.Title,
            Font,
            new Rectangle(icon.Right + 6, bounds.Y, Math.Max(8, textRight - icon.Right - 6), bounds.Height),
            tab.IsActive ? Color.White : Color.FromArgb(198, 200, 204),
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (showClose)
        {
            bool hot = _closeHotIndex == index;
            if (hot)
            {
                using var hotFill = new SolidBrush(Color.FromArgb(70, 74, 80));
                graphics.FillEllipse(hotFill, close);
            }
            using var pen = new Pen(hot ? Color.White : Color.FromArgb(170, 172, 176), 1.35f);
            int pad = 4;
            graphics.DrawLine(
                pen,
                close.Left + pad,
                close.Top + pad,
                close.Right - pad,
                close.Bottom - pad);
            graphics.DrawLine(
                pen,
                close.Right - pad,
                close.Top + pad,
                close.Left + pad,
                close.Bottom - pad);
        }

        if (_dragging && _dropIndex == index && index != _pressedIndex)
        {
            using var marker = new SolidBrush(AccentColor);
            int mx = _dropIndex > _pressedIndex ? bounds.Right - 2 : bounds.Left;
            graphics.FillRectangle(marker, mx, bounds.Y + 4, 2, bounds.Height - 6);
        }
    }

    private void PaintPlus(Graphics graphics, Rectangle bounds, bool hot)
    {
        using var fill = new SolidBrush(hot ? HoverColor : Color.FromArgb(32, 34, 38));
        graphics.FillEllipse(fill, bounds);
        using var pen = new Pen(hot ? Color.White : Color.FromArgb(200, 202, 206), 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        int cx = bounds.X + bounds.Width / 2;
        int cy = bounds.Y + bounds.Height / 2;
        graphics.DrawLine(pen, cx - 6, cy, cx + 6, cy);
        graphics.DrawLine(pen, cx, cy - 6, cx, cy + 6);
    }

    private static void PaintChevron(
        Graphics graphics,
        Rectangle bounds,
        bool hot,
        bool left,
        bool enabled)
    {
        if (bounds.Width <= 0)
        {
            return;
        }
        using var fill = new SolidBrush(hot && enabled ? HoverColor : Color.FromArgb(30, 32, 36));
        graphics.FillEllipse(fill, bounds.X + 1, bounds.Y + 2, bounds.Width - 2, bounds.Height - 4);
        using var pen = new Pen(
            enabled ? (hot ? Color.White : Color.FromArgb(180, 182, 186)) : Color.FromArgb(80, 82, 86),
            1.5f);
        int cx = bounds.X + bounds.Width / 2;
        int cy = bounds.Y + bounds.Height / 2;
        int dir = left ? -1 : 1;
        graphics.DrawLines(
            pen,
            new Point[]
            {
                new(cx + 3 * dir, cy - 5),
                new(cx - 3 * dir, cy),
                new(cx + 3 * dir, cy + 5),
            });
    }

    private static void PaintCadIcon(Graphics graphics, Rectangle bounds, bool dxf)
    {
        Color ink = dxf ? Color.FromArgb(232, 168, 72) : Color.FromArgb(72, 186, 214);
        using var fill = new SolidBrush(Color.FromArgb(36, 38, 42));
        using var pen = new Pen(ink, 1.2f);
        graphics.FillRectangle(fill, bounds);
        graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        graphics.DrawLine(pen, bounds.Right - 5, bounds.Y, bounds.Right - 1, bounds.Y + 4);
        using var mark = new Pen(ink, 1.15f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int x = bounds.X + 3;
        int y = bounds.Bottom - 4;
        graphics.DrawLines(
            mark,
            new Point[]
            {
                new(x, y - 5),
                new(x, y),
                new(x + 5, y),
            });
    }

    private static GraphicsPath CreateTabPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
        int x = bounds.X;
        int y = bounds.Y;
        int w = bounds.Width;
        int h = bounds.Height;
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddLine(x + w, y + r, x + w, y + h);
        path.AddLine(x + w, y + h, x, y + h);
        path.CloseFigure();
        return path;
    }

    private static Rectangle CloseBounds(Rectangle tab) =>
        new(tab.Right - 22, tab.Y + (tab.Height - 16) / 2, 16, 16);

    private float Hover(int index) =>
        (uint)index < (uint)_hover.Length ? _hover[index] : 0f;

    private static Color Lerp(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }

    private static bool IsDxf(string path) =>
        path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase);

    private readonly record struct TabVisual(Guid Id, string Title, bool IsDxf, bool IsActive);

    private readonly record struct LayoutMetrics(
        int TabWidth,
        Rectangle TabsClip,
        Rectangle PlusBounds,
        Rectangle LeftButton,
        Rectangle RightButton,
        bool Overflow,
        int MaxScroll);
}
