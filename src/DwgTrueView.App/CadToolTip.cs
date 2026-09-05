namespace DwgTrueView.App;

internal sealed record CommandTip(
    string Title,
    string Description,
    string Shortcut = "",
    string Command = "")
{
    public string CommandCode
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Command))
            {
                return Command;
            }

            return new string(Title.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }
    }

    public static CommandTip? FromItem(ToolStripItem? item)
    {
        if (item is null or ToolStripSeparator)
        {
            return null;
        }

        if (item.Tag is CommandTip tagged)
        {
            return tagged;
        }

        if (string.IsNullOrWhiteSpace(item.ToolTipText))
        {
            return null;
        }

        string[] parts = item.ToolTipText.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string title = parts.Length > 0 ? parts[0] : item.Text ?? string.Empty;
        string body = parts.Length > 1 ? string.Join(Environment.NewLine, parts.Skip(1)) : string.Empty;
        return string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)
            ? null
            : new CommandTip(title, body);
    }
}

/// <summary>
/// AutoCAD / SolidWorks style command tip. ToolStrip items use MouseHover
/// and a modeless popup (not WinForms ToolTip) so the strip's own tooltip
/// host cannot swallow the main-toolbar tips.
/// </summary>
internal sealed class CadToolTip : IDisposable
{
    private const int MaxText = 280;
    private const int ShowDelayMs = 800;

    private static readonly Color TipFill = Color.FromArgb(248, 248, 248);
    private static readonly Color TipBorder = Color.FromArgb(168, 168, 168);
    private static readonly Color TitleColor = Color.FromArgb(20, 20, 20);
    private static readonly Color ShortcutColor = Color.FromArgb(70, 70, 70);
    private static readonly Color BodyColor = Color.FromArgb(48, 48, 48);
    private static readonly Color FooterColor = Color.FromArgb(96, 96, 96);
    private static readonly Color RuleColor = Color.FromArgb(210, 210, 210);

    private readonly Control _host;
    private readonly Func<Point, HoverTarget?>? _hit;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly TipWindow _window;
    private readonly System.Windows.Forms.Timer _watch;
    private HoverTarget? _current;
    private bool _disposed;

    private readonly record struct HoverTarget(
        object Key,
        CommandTip Tip,
        Rectangle ScreenAnchor,
        bool PlaceBeside);

    private CadToolTip(Control host, Func<Point, HoverTarget?>? hit)
    {
        _host = host;
        _hit = hit;
        _window = new TipWindow();
        _timer = new System.Windows.Forms.Timer
        {
            Interval = ShowDelayMs,
        };
        _timer.Tick += OnTick;
        _watch = new System.Windows.Forms.Timer { Interval = 50 };
        _watch.Tick += OnWatch;
        host.Disposed += (_, _) => Dispose();
        if (hit is not null)
        {
            host.MouseMove += OnHostMouseMove;
            host.MouseLeave += OnHostMouseLeave;
            host.MouseDown += (_, _) => HideTip();
        }
    }

    public static CadToolTip Attach(ToolStrip strip)
    {
        strip.ShowItemToolTips = false;
        var tips = new CadToolTip(strip, hit: null);
        foreach (ToolStripItem item in strip.Items)
        {
            BindItem(tips, strip, item);
        }

        strip.ItemAdded += (_, e) =>
        {
            if (e.Item is not null)
            {
                BindItem(tips, strip, e.Item);
            }
        };
        strip.MouseDown += (_, _) => tips.HideTip();
        strip.MouseLeave += (_, _) =>
        {
            Point local = strip.PointToClient(Control.MousePosition);
            if (!strip.ClientRectangle.Contains(local))
            {
                tips.Hide();
            }
        };
        return tips;
    }

    public static CadToolTip Attach(Control host, Func<Point, (CommandTip Tip, Rectangle Anchor)?> hit)
    {
        return new CadToolTip(host, point =>
        {
            (CommandTip Tip, Rectangle Anchor)? found = hit(point);
            if (found is null)
            {
                return null;
            }

            Rectangle screen = host.RectangleToScreen(found.Value.Anchor);
            return new HoverTarget(
                found.Value.Tip.Title + found.Value.Tip.Shortcut + found.Value.Tip.Description,
                found.Value.Tip,
                screen,
                PlaceBeside: false);
        });
    }

    public static bool IsPointerOverAnyTip() => TipWindow.IsPointerOverAny();

    public void Hide()
    {
        _current = null;
        _timer.Stop();
        _watch.Stop();
        HideTip();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _watch.Stop();
        _window.Dismiss(immediate: true);
        _timer.Dispose();
        _watch.Dispose();
        _window.Dispose();
    }

    private static void BindItem(CadToolTip tips, ToolStrip strip, ToolStripItem item)
    {
        if (item is ToolStripSeparator)
        {
            return;
        }

        item.AutoToolTip = false;
        item.MouseEnter += (_, _) => tips.BeginItemTip(strip, item);
        item.MouseLeave += (_, _) =>
        {
            Point screen = Control.MousePosition;
            if (!strip.RectangleToScreen(item.Bounds).Contains(screen))
            {
                tips.CancelItemTip(item);
            }
        };
        item.MouseDown += (_, _) => tips.HideTip();
        if (item is ToolStripButton button)
        {
            button.CheckedChanged += (_, _) =>
            {
                if (button.Checked)
                {
                    tips.CancelItemTip(button);
                }
            };
        }
        else if (item is ToolStripMenuItem menu)
        {
            menu.CheckedChanged += (_, _) =>
            {
                if (menu.Checked)
                {
                    tips.CancelItemTip(menu);
                }
            };
        }
    }

    private void BeginItemTip(ToolStrip strip, ToolStripItem item)
    {
        if (IsActive(item))
        {
            CancelItemTip(item);
            return;
        }

        CommandTip? tip = CommandTip.FromItem(item);
        if (tip is null)
        {
            return;
        }

        var target = new HoverTarget(
            item,
            tip,
            strip.RectangleToScreen(item.Bounds),
            PlaceBeside: strip is ToolStripDropDown);
        if (_current is { } current && Equals(current.Key, target.Key))
        {
            return;
        }

        _current = target;
        HideTip();
        _timer.Stop();
        _timer.Start();
        _watch.Start();
    }

    private void CancelItemTip(ToolStripItem item)
    {
        if (_current is not { } current || !Equals(current.Key, item))
        {
            return;
        }

        _current = null;
        _timer.Stop();
        HideTip();
    }

    private void OnHostMouseMove(object? sender, MouseEventArgs e)
    {
        if (_hit is null)
        {
            return;
        }

        HoverTarget? next = _hit(e.Location);
        if (_current is { } current && next is { } same && Equals(current.Key, same.Key))
        {
            return;
        }

        _current = next;
        HideTip();
        _timer.Stop();
        if (next is not null)
        {
            _timer.Start();
            _watch.Start();
        }
        else
        {
            _watch.Stop();
        }
    }

    private void OnHostMouseLeave(object? sender, EventArgs e)
    {
        Point local = _host.PointToClient(Control.MousePosition);
        if (_host.ClientRectangle.Contains(local))
        {
            return;
        }

        _current = null;
        _timer.Stop();
        _watch.Stop();
        HideTip();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        if (_current is not { } target || !IsPointerOn(target))
        {
            _current = null;
            return;
        }

        ShowTip(target);
    }

    private void OnWatch(object? sender, EventArgs e)
    {
        if (_current is not { } target)
        {
            if (!_window.IsDisplayed)
            {
                _watch.Stop();
            }

            return;
        }

        if (IsPointerOn(target) && !(target.Key is ToolStripItem item && IsActive(item)))
        {
            return;
        }

        _current = null;
        _timer.Stop();
        HideTip();
    }

    private bool IsPointerOn(HoverTarget target)
    {
        Point pointer = Control.MousePosition;
        if (target.Key is ToolStripItem item)
        {
            ToolStrip? strip = item.GetCurrentParent();
            return strip is { IsDisposed: false, Visible: true }
                && item.Available
                && strip.RectangleToScreen(item.Bounds).Contains(pointer);
        }

        if (_hit is null || !_host.IsHandleCreated)
        {
            return target.ScreenAnchor.Contains(pointer);
        }

        HoverTarget? next = _hit(_host.PointToClient(pointer));
        return next is { } found && Equals(found.Key, target.Key);
    }

    private void ShowTip(HoverTarget target)
    {
        if (target.Key is ToolStripItem item && IsActive(item))
        {
            return;
        }

        _current = target;
        _window.Present(target.Tip, target.ScreenAnchor, target.PlaceBeside);
        _watch.Start();
    }

    private static bool IsActive(ToolStripItem item) =>
        item.Pressed
        || (item is ToolStripButton button && button.Checked)
        || (item is ToolStripMenuItem menu && menu.Checked);

    private void HideTip()
    {
        _window.Dismiss();
    }

    private sealed class TipWindow : Form
    {
        private readonly Font _titleFont = new("Segoe UI", 9.75f, FontStyle.Bold);
        private readonly Font _shortcutFont = new("Segoe UI Semibold", 8.25f, FontStyle.Bold);
        private readonly Font _bodyFont = new("Segoe UI", 8.25f);
        private readonly Font _footerFont = new("Segoe UI", 8f);
        private readonly CadToolTipFade _fade;
        private CommandTip _tip = new(string.Empty, string.Empty);

        public TipWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = TipFill;
            DoubleBuffered = true;
            AllowTransparency = true;
            Opacity = 0;
            _fade = new CadToolTipFade(this);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= 0x00020000;
                parameters.ExStyle |= 0x08000000;
                return parameters;
            }
        }

        public bool IsDisplayed => Visible || _fade.IsRunning;

        public static bool IsPointerOverAny()
        {
            Point pointer = Control.MousePosition;
            foreach (Form form in Application.OpenForms)
            {
                if (form is TipWindow tip && tip.Visible && tip.Bounds.Contains(pointer))
                {
                    return true;
                }
            }

            return false;
        }

        public void Present(CommandTip tip, Rectangle screenAnchor, bool placeBeside = false)
        {
            _tip = tip;
            Size size = MeasureTip(tip);
            Rectangle work = Screen.FromPoint(screenAnchor.Location).WorkingArea;
            int x;
            int y;
            if (placeBeside)
            {
                x = screenAnchor.Right + 8;
                y = screenAnchor.Top;
                if (x + size.Width > work.Right - 4)
                {
                    x = screenAnchor.Left - size.Width - 8;
                }

                y = Math.Clamp(y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - size.Height - 4));
            }
            else
            {
                x = screenAnchor.Left;
                y = screenAnchor.Bottom + 4;
                if (y + size.Height > work.Bottom)
                {
                    y = screenAnchor.Top - size.Height - 2;
                }
            }

            x = Math.Clamp(x, work.Left + 4, Math.Max(work.Left + 4, work.Right - size.Width - 4));

            Size = size;
            Location = new Point(x, y);
            Invalidate();
            if (!Visible)
            {
                Opacity = 0;
                Show();
            }

            _fade.Play(1);
        }

        public void Dismiss(bool immediate = false)
        {
            if (!Visible && !_fade.IsRunning)
            {
                return;
            }

            if (immediate)
            {
                _fade.Snap(0);
                Hide();
                return;
            }

            _fade.Play(0, () =>
            {
                if (Opacity <= 0.02)
                {
                    Hide();
                    Opacity = 0;
                }
            });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            Rectangle body = new(0, 0, Width - 1, Height - 1);
            using var fill = new SolidBrush(TipFill);
            using var border = new Pen(TipBorder);
            graphics.FillRectangle(fill, body);
            graphics.DrawRectangle(border, body);

            int x = 12;
            int y = 10;
            string shortcut = string.IsNullOrWhiteSpace(_tip.Shortcut) ? string.Empty : $"[{_tip.Shortcut}]";
            Size shortcutSize = string.IsNullOrEmpty(shortcut)
                ? Size.Empty
                : Measure(shortcut, _shortcutFont, 160, wrap: false);
            var titleBounds = new Rectangle(x, y, Width - 24 - (shortcutSize.Width > 0 ? shortcutSize.Width + 10 : 0), _titleFont.Height + 2);
            TextRenderer.DrawText(
                graphics,
                _tip.Title,
                _titleFont,
                titleBounds,
                TitleColor,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (shortcutSize.Width > 0)
            {
                TextRenderer.DrawText(
                    graphics,
                    shortcut,
                    _shortcutFont,
                    new Rectangle(Width - 12 - shortcutSize.Width, y + 2, shortcutSize.Width, shortcutSize.Height),
                    ShortcutColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }

            y = titleBounds.Bottom + 6;
            if (!string.IsNullOrWhiteSpace(_tip.Description))
            {
                Size bodySize = Measure(_tip.Description, _bodyFont, MaxText, wrap: true);
                TextRenderer.DrawText(
                    graphics,
                    _tip.Description,
                    _bodyFont,
                    new Rectangle(x, y, Width - 24, bodySize.Height),
                    BodyColor,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                y += bodySize.Height + 10;
            }

            using var rule = new Pen(RuleColor);
            graphics.DrawLine(rule, x, y, Width - 12, y);
            y += 8;
            string code = _tip.CommandCode;
            if (!string.IsNullOrWhiteSpace(code))
            {
                TextRenderer.DrawText(
                    graphics,
                    code,
                    _shortcutFont,
                    new Rectangle(x, y, Width - 24, _shortcutFont.Height + 2),
                    TitleColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                y += _shortcutFont.Height + 4;
            }

            TextRenderer.DrawText(
                graphics,
                "Press F1 for more help",
                _footerFont,
                new Rectangle(x, y, Width - 24, _footerFont.Height + 2),
                FooterColor,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fade.Dispose();
                _titleFont.Dispose();
                _shortcutFont.Dispose();
                _bodyFont.Dispose();
                _footerFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private Size MeasureTip(CommandTip tip)
        {
            Size title = Measure(tip.Title, _titleFont, MaxText, wrap: false);
            string shortcut = string.IsNullOrWhiteSpace(tip.Shortcut) ? string.Empty : $"[{tip.Shortcut}]";
            Size badge = string.IsNullOrEmpty(shortcut)
                ? Size.Empty
                : Measure(shortcut, _shortcutFont, 160, wrap: false);
            Size body = string.IsNullOrWhiteSpace(tip.Description)
                ? Size.Empty
                : Measure(tip.Description, _bodyFont, MaxText, wrap: true);
            Size code = Measure(tip.CommandCode, _shortcutFont, MaxText, wrap: false);
            Size help = Measure("Press F1 for more help", _footerFont, MaxText, wrap: false);
            int header = title.Width + (badge.Width > 0 ? 12 + badge.Width : 0);
            int width = Math.Max(Math.Max(header, body.Width), Math.Max(code.Width, help.Width)) + 28;
            int height = 22 + title.Height + 8 + help.Height + 12;
            if (body.Height > 0)
            {
                height += body.Height + 10;
            }

            if (!string.IsNullOrWhiteSpace(tip.CommandCode))
            {
                height += code.Height + 4;
            }

            return new Size(Math.Clamp(width, 200, 360), height);
        }

        private static Size Measure(string text, Font font, int width, bool wrap) =>
            TextRenderer.MeasureText(
                text,
                font,
                new Size(width, 0),
                (wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine)
                | TextFormatFlags.NoPadding
                | TextFormatFlags.NoPrefix);
    }
}
