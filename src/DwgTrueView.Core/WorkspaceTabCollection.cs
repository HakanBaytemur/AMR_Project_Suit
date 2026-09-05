namespace DwgTrueView.Core;

/// <summary>
/// One in-memory drawing tab: parsed CAD payload plus independent camera
/// and layer-visibility state. Switching tabs reuses this object; it does
/// not re-read the source file.
/// </summary>
public sealed class DrawingWorkspace
{
    public DrawingWorkspace(PackedCadDrawing drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        Id = Guid.NewGuid();
        Drawing = drawing;
        Camera = new ViewCamera2D();
        LayerVisibility = drawing.Layers.Span
            .ToArray()
            .Select(static layer => layer.IsInitiallyVisible)
            .ToArray();
        FileName = Path.GetFileName(drawing.SourcePath);
    }

    public Guid Id { get; }
    public PackedCadDrawing Drawing { get; }
    public ViewCamera2D Camera { get; }
    public bool[] LayerVisibility { get; }
    public string FileName { get; }
    public string SourcePath => Drawing.SourcePath;
}

/// <summary>
/// Browser-style tab list. Adding a drawing always creates a new tab and
/// activates it; close selects the neighbor to the right (or left if last).
/// </summary>
public sealed class WorkspaceTabCollection
{
    private readonly List<DrawingWorkspace> _tabs = [];

    public IReadOnlyList<DrawingWorkspace> Tabs => _tabs;
    public DrawingWorkspace? Active { get; private set; }
    public int Count => _tabs.Count;

    public event EventHandler? Changed;

    public DrawingWorkspace Add(PackedCadDrawing drawing)
    {
        var tab = new DrawingWorkspace(drawing);
        _tabs.Add(tab);
        Active = tab;
        Changed?.Invoke(this, EventArgs.Empty);
        return tab;
    }

    public bool Activate(Guid id)
    {
        DrawingWorkspace? tab = Find(id);
        if (tab is null || ReferenceEquals(Active, tab))
        {
            return false;
        }

        Active = tab;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Close(Guid id)
    {
        int index = _tabs.FindIndex(tab => tab.Id == id);
        if (index < 0)
        {
            return false;
        }

        bool wasActive = Active?.Id == id;
        _tabs.RemoveAt(index);
        if (_tabs.Count == 0)
        {
            Active = null;
        }
        else if (wasActive)
        {
            Active = _tabs[Math.Min(index, _tabs.Count - 1)];
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Move(Guid id, int newIndex)
    {
        int current = _tabs.FindIndex(tab => tab.Id == id);
        if (current < 0)
        {
            return false;
        }

        newIndex = Math.Clamp(newIndex, 0, _tabs.Count - 1);
        if (newIndex == current)
        {
            return false;
        }

        DrawingWorkspace tab = _tabs[current];
        _tabs.RemoveAt(current);
        _tabs.Insert(newIndex, tab);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Insert(DrawingWorkspace tab, int index, bool activate)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (Find(tab.Id) is not null)
        {
            if (activate)
            {
                Activate(tab.Id);
            }
            return;
        }

        index = Math.Clamp(index, 0, _tabs.Count);
        _tabs.Insert(index, tab);
        if (activate || Active is null)
        {
            Active = tab;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public int IndexOf(Guid id) => _tabs.FindIndex(tab => tab.Id == id);

    public bool ActivateRelative(int delta)
    {
        if (Active is null || _tabs.Count < 2)
        {
            return false;
        }

        int index = _tabs.IndexOf(Active);
        int next = (index + delta) % _tabs.Count;
        if (next < 0)
        {
            next += _tabs.Count;
        }

        return Activate(_tabs[next].Id);
    }

    public DrawingWorkspace? Find(Guid id)
    {
        foreach (DrawingWorkspace tab in _tabs)
        {
            if (tab.Id == id)
            {
                return tab;
            }
        }

        return null;
    }
}
