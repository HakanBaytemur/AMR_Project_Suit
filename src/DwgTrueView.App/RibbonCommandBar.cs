namespace DwgTrueView.App;

/// <summary>
/// Main ribbon row. Collapses groups from the right when the window
/// is too narrow, matching AutoCAD's overflow tiles.
/// </summary>
internal sealed class RibbonCommandBar : Panel
{
    private readonly FlowLayoutPanel _flow;
    private readonly List<RibbonCommandGroup> _groups = [];
    private bool _relayouting;
    private int _appliedCollapse = -1;
    private int _lastAvailable = -1;

    public RibbonCommandBar()
    {
        Height = 70;
        BackColor = RibbonCommandGroup.BarColor;
        Padding = new Padding(2, 0, 2, 0);
        DoubleBuffered = true;

        _flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = RibbonCommandGroup.BarColor,
            AutoScroll = false,
            AutoScrollMinSize = Size.Empty,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        _flow.Resize += (_, _) => Relayout();
        Controls.Add(_flow);
    }

    public void AddGroups(params (string Title, ToolStripItem[] Items)[] groups)
    {
        _flow.SuspendLayout();
        for (int index = 0; index < groups.Length; index++)
        {
            var group = new RibbonCommandGroup(
                groups[index].Title,
                groups[index].Items,
                showDivider: index < groups.Length - 1);
            _groups.Add(group);
            _flow.Controls.Add(group);
        }

        _flow.ResumeLayout(true);
        Relayout();
    }

    public ToolStripButton? FindCommand(string name)
    {
        foreach (RibbonCommandGroup group in _groups)
        {
            ToolStripButton? match = group.FindCommand(name);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Relayout();
    }

    private void Relayout()
    {
        if (_relayouting || _groups.Count == 0 || _flow.ClientSize.Width <= 0)
        {
            return;
        }

        _relayouting = true;
        try
        {
            RelayoutCore();
        }
        finally
        {
            _relayouting = false;
        }
    }

    private void RelayoutCore()
    {
        if (_groups.Count == 0 || _flow.ClientSize.Width <= 0)
        {
            return;
        }

        int available = _flow.ClientSize.Width;
        int collapseCount = 0;
        while (collapseCount < _groups.Count && Measure(collapseCount) > available)
        {
            collapseCount++;
        }

        if (collapseCount == _appliedCollapse && available == _lastAvailable)
        {
            return;
        }

        _appliedCollapse = collapseCount;
        _lastAvailable = available;

        _flow.SuspendLayout();
        for (int index = 0; index < _groups.Count; index++)
        {
            bool collapse = index >= _groups.Count - collapseCount;
            _groups[index].SetCollapsed(collapse);
        }

        int used = Measure(collapseCount);
        _flow.AutoScroll = used > available;
        _flow.AutoScrollMinSize = _flow.AutoScroll ? new Size(used, 0) : Size.Empty;
        _flow.ResumeLayout(true);
    }

    private int Measure(int collapseCount)
    {
        int width = 0;
        for (int index = 0; index < _groups.Count; index++)
        {
            bool collapse = index >= _groups.Count - collapseCount;
            width += collapse ? _groups[index].CollapsedWidth : _groups[index].ExpandedWidth;
        }

        return width;
    }
}
