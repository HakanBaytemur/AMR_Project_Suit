namespace DwgTrueView.App;

/// <summary>
/// CAD-style alias buffer: single keys (L, M, F) fire immediately;
/// two-letter aliases (CO, CP, RO, TR) wait briefly for the second character.
/// </summary>
internal sealed class CadAliasBuffer : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;
    private string _buffer = string.Empty;

    public CadAliasBuffer(int lingerMs = 900)
    {
        _timer = new System.Windows.Forms.Timer { Interval = lingerMs };
        _timer.Tick += (_, _) => Clear();
    }

    public event Action<string>? Matched;

    public bool Feed(Keys key)
    {
        if (key is < Keys.A or > Keys.Z)
        {
            Clear();
            return false;
        }

        _buffer += (char)('A' + (key - Keys.A));
        _timer.Stop();
        if (_buffer is "L" or "M" or "F" or "CO" or "CP" or "RO" or "TR")
        {
            string alias = _buffer;
            Clear();
            Matched?.Invoke(alias);
            return true;
        }

        if (_buffer is "C" or "R" or "T")
        {
            _timer.Start();
            return true;
        }

        Clear();
        return false;
    }

    public void Clear()
    {
        _buffer = string.Empty;
        _timer.Stop();
    }

    public void Dispose() => _timer.Dispose();
}

internal static class CadShortcuts
{
    public static bool IsTextEditing(Control? control)
    {
        while (control is not null)
        {
            if (control is TextBoxBase or ComboBox)
            {
                return true;
            }

            if (control is DataGridView grid && grid.IsCurrentCellInEditMode)
            {
                return true;
            }

            control = control.Parent;
        }

        return false;
    }

    public static string? CommandName(string alias) => alias switch
    {
        "L" => "Straight Route",
        "M" => "Move",
        "F" => "Add Radius",
        "CO" or "CP" => "Copy to Clipboard",
        "RO" => "Rotate",
        "TR" => "Trim",
        _ => null,
    };
}
