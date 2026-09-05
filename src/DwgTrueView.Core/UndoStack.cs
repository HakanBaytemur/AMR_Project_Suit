namespace DwgTrueView.Core;

public interface IUndoAction
{
    string Name { get; }
    void Undo();
    void Redo();
}

public sealed class DelegateUndoAction : IUndoAction
{
    private readonly Action _undo;
    private readonly Action _redo;

    public DelegateUndoAction(string name, Action undo, Action redo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(redo);
        Name = name;
        _undo = undo;
        _redo = redo;
    }

    public string Name { get; }

    public void Undo() => _undo();

    public void Redo() => _redo();
}

/// <summary>
/// Last-in first-out undo/redo history. A new action clears the redo list.
/// Oldest undo entries drop when the cap is hit.
/// </summary>
public sealed class UndoStack
{
    public const int MaximumEntries = 100;

    private readonly List<IUndoAction> _undo = [];
    private readonly List<IUndoAction> _redo = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int Count => _undo.Count;
    public string? NextName => CanUndo ? _undo[^1].Name : null;
    public string? NextRedoName => CanRedo ? _redo[^1].Name : null;

    public event EventHandler? Changed;

    public void Push(IUndoAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _undo.Add(action);
        if (_undo.Count > MaximumEntries)
        {
            _undo.RemoveAt(0);
        }
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool TryUndo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        IUndoAction action = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        action.Undo();
        _redo.Add(action);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool TryRedo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        IUndoAction action = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        action.Redo();
        _undo.Add(action);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0)
        {
            return;
        }

        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
