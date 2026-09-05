using System.Runtime.InteropServices;

namespace DwgTrueView.App;

/// <summary>
/// Uses DWM so the native caption matches the dark CAD chrome.
/// </summary>
internal static class DarkTitleBar
{
    private const int UseImmersiveDarkMode = 20;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    public static void Apply(Form form)
    {
        if (!form.IsHandleCreated)
        {
            return;
        }

        try
        {
            int useDark = 1;
            _ = DwmSetWindowAttribute(form.Handle, UseImmersiveDarkMode, ref useDark, sizeof(int));

            int caption = ToColorRef(Color.FromArgb(0x22, 0x29, 0x33));
            _ = DwmSetWindowAttribute(form.Handle, CaptionColor, ref caption, sizeof(int));

            int text = ToColorRef(Color.FromArgb(209, 209, 209));
            _ = DwmSetWindowAttribute(form.Handle, TextColor, ref text, sizeof(int));

            var margins = new Margins { Top = 1 };
            _ = DwmExtendFrameIntoClientArea(form.Handle, ref margins);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static int ToColorRef(Color color) =>
        color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }
}
