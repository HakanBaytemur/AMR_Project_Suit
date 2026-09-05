namespace DwgTrueView.App;

internal sealed record CommandTip(string Title, string Description, string Shortcut = "")
{
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
/// Standard WinForms ToolTip with OwnerDraw styling — the practical
/// equivalent of a WPF ToolTip template: title, optional [shortcut],
/// and a short description after the system hover delay.
/// </summary>
internal sealed class CadToolTip : IDisposable
{
    private static readonly Color TipFill = Color.FromArgb(38, 40, 46);
    private static readonly Color TipBorder = Color.FromArgb(86, 90, 98);
    private static readonly Color TitleColor = Color.FromArgb(245, 245, 247);
    private static readonly Color ShortcutColor = Color.FromArgb(130, 190, 255);
    private static readonly Color BodyColor = Color.FromArgb(176, 180, 188);
    private static readonly Color RuleColor = Color.FromArgb(68, 72, 80);

    private readonly Control _host;
    private readonly Func<Point, HoverTarget?> _hit;
    private readonly ToolTip _tips;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Font _titleFont = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _shortcutFont = new("Segoe UI", 8.25f, FontStyle.Bold);
    private readonly Font _bodyFont = new("Segoe UI", 8.25f);
    private HoverTarget? _current;
    private CommandTip _tip = new(string.Empty, string.Empty);
    private bool _visible;
    private bool _disposed;

    private readonly record struct HoverTarget(object Key, CommandTip Tip, Point ShowAt);

    private CadToolTip(Control host, Func<Point, HoverTarget?> hit)
    {
        _host = host;
        _hit = hit;
        _tips = new ToolTip
        {
            OwnerDraw = true,
            ShowAlways = true,
            UseAnimation = true,
            UseFading = true,
        };
        _tips.Popup += OnPopup;
        _tips.Draw += OnDraw;
        _timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Clamp(SystemInformation.MouseHoverTime, 400, 700),
        };
        _timer.Tick += OnTick;
        host.MouseMove += OnMouseMove;
        host.MouseLeave += OnMouseLeave;
        host.MouseDown += OnMouseDown;
        host.Disposed += (_, _) => Dispose();
    }

    public static CadToolTip Attach(ToolStrip strip)
    {
        strip.ShowItemToolTips = false;
        foreach (ToolStripItem item in strip.Items)
        {
            item.AutoToolTip = false;
        }

        return new CadToolTip(strip, point =>
        {
            ToolStripItem? item = strip.GetItemAt(point);
            CommandTip? tip = CommandTip.FromItem(item);
            return tip is null || item is null
                ? null
                : new HoverTarget(item, tip, new Point(item.Bounds.Left, item.Bounds.Bottom + 6));
        });
    }

    public static CadToolTip Attach(Control host, Func<Point, (CommandTip Tip, Rectangle Anchor)?> hit)
    {
        return new CadToolTip(host, point =>
        {
            (CommandTip Tip, Rectangle Anchor)? found = hit(point);
            return found is null
                ? null
                : new HoverTarget(
                    found.Value.Tip.Title + found.Value.Tip.Shortcut + found.Value.Tip.Description,
                    found.Value.Tip,
                    new Point(found.Value.Anchor.Left, found.Value.Anchor.Bottom + 6));
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        HideTip();
        _timer.Dispose();
        _tips.Dispose();
        _titleFont.Dispose();
        _shortcutFont.Dispose();
        _bodyFont.Dispose();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
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
        }
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        Point local = _host.PointToClient(Control.MousePosition);
        if (_host.ClientRectangle.Contains(local))
        {
            return;
        }

        _current = null;
        _timer.Stop();
        HideTip();
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        _timer.Stop();
        HideTip();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        if (_current is not { } target)
        {
            return;
        }

        _tip = target.Tip;
        _visible = true;
        string token = string.IsNullOrWhiteSpace(_tip.Description) ? _tip.Title : _tip.Description;
        _tips.Show(token, _host, target.ShowAt, 12000);
    }

    private void HideTip()
    {
        if (!_visible)
        {
            return;
        }

        _visible = false;
        _tips.Hide(_host);
    }

    private void OnPopup(object? sender, PopupEventArgs e)
    {
        const int maxText = 280;
        Size title = Measure(_tip.Title, _titleFont, maxText, wrap: false);
        string shortcut = ShortcutText(_tip);
        Size badge = string.IsNullOrEmpty(shortcut)
            ? Size.Empty
            : Measure(shortcut, _shortcutFont, 160, wrap: false);
        Size body = string.IsNullOrWhiteSpace(_tip.Description)
            ? Size.Empty
            : Measure(_tip.Description, _bodyFont, maxText, wrap: true);

        int header = title.Width + (badge.Width > 0 ? 12 + badge.Width : 0);
        int width = Math.Max(header, body.Width) + 22;
        int height = 16 + title.Height;
        if (body.Height > 0)
        {
            height += 10 + body.Height;
        }

        e.ToolTipSize = new Size(Math.Clamp(width, 140, 340), height);
    }

    private void OnDraw(object? sender, DrawToolTipEventArgs e)
    {
        using var fill = new SolidBrush(TipFill);
        e.Graphics.FillRectangle(fill, e.Bounds);
        using var border = new Pen(TipBorder);
        Rectangle edge = e.Bounds;
        edge.Width -= 1;
        edge.Height -= 1;
        e.Graphics.DrawRectangle(border, edge);

        var titleBounds = new Rectangle(e.Bounds.X + 11, e.Bounds.Y + 8, e.Bounds.Width - 22, _titleFont.Height + 2);
        string shortcut = ShortcutText(_tip);
        if (shortcut.Length > 0)
        {
            Size badge = Measure(shortcut, _shortcutFont, 160, wrap: false);
            var badgeBounds = new Rectangle(
                e.Bounds.Right - 11 - badge.Width,
                titleBounds.Y + 1,
                badge.Width,
                badge.Height);
            titleBounds.Width = Math.Max(40, badgeBounds.Left - titleBounds.Left - 8);
            TextRenderer.DrawText(
                e.Graphics,
                shortcut,
                _shortcutFont,
                badgeBounds,
                ShortcutColor,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        TextRenderer.DrawText(
            e.Graphics,
            _tip.Title,
            _titleFont,
            titleBounds,
            TitleColor,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        if (string.IsNullOrWhiteSpace(_tip.Description))
        {
            return;
        }

        int ruleY = titleBounds.Bottom + 4;
        using var rule = new Pen(RuleColor);
        e.Graphics.DrawLine(rule, e.Bounds.X + 11, ruleY, e.Bounds.Right - 11, ruleY);

        var bodyBounds = new Rectangle(
            e.Bounds.X + 11,
            ruleY + 6,
            e.Bounds.Width - 22,
            e.Bounds.Bottom - ruleY - 10);
        TextRenderer.DrawText(
            e.Graphics,
            _tip.Description,
            _bodyFont,
            bodyBounds,
            BodyColor,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private static string ShortcutText(CommandTip tip) =>
        string.IsNullOrWhiteSpace(tip.Shortcut) ? string.Empty : $"[{tip.Shortcut}]";

    private static Size Measure(string text, Font font, int width, bool wrap) =>
        TextRenderer.MeasureText(
            text,
            font,
            new Size(width, 0),
            (wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine) | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
}
