using DwgTrueView.Core;

namespace DwgTrueView.App;

/// <summary>
/// Modeless, resizable layer manager that floats over the drawing canvas.
/// </summary>
internal sealed class LayerPropertiesForm : Form
{
    private readonly LayerPropertiesPanel _panel = new();

    public LayerPropertiesForm()
    {
        Text = "Layer Properties";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(260, 180);
        Size = new Size(340, 360);
        BackColor = Color.FromArgb(37, 39, 43);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(0);
        Controls.Add(_panel);
    }

    public event Action<int, bool>? VisibilityChanged
    {
        add => _panel.VisibilityChanged += value;
        remove => _panel.VisibilityChanged -= value;
    }

    public void Bind(IReadOnlyList<CadLayer> layers, bool[]? visibility = null) =>
        _panel.Bind(layers, visibility);

    public void ClearLayers() => _panel.ClearLayers();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryEnableDarkTitleBar();
    }

    private void TryEnableDarkTitleBar()
    {
        try
        {
            int useDark = 1;
            _ = DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
