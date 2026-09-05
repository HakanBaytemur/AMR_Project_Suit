using System.Runtime.InteropServices;

namespace DwgTrueView.App;

internal enum CaptionRegion
{
    Client,
    Drag,
    SystemMenu,
    Minimize,
    Maximize,
    Close,
}

/// <summary>
/// Merges the client chrome into the native caption so the mini toolbar
/// sits on the title bar, like SolidWorks.
/// </summary>
internal static class CaptionFrame
{
    private const int WmNcCalcSize = 0x0083;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcActivate = 0x0086;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtSysMenu = 3;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int SmCxFrame = 32;
    private const int SmCxPaddedBorder = 92;
    private const int SwpFrameChanged = 0x0020;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoZOrder = 0x0004;
    private const int ResizeGrip = 8;

    public static bool Process(Form form, RibbonMenu ribbon, ref Message message)
    {
        if (message.Msg == WmNcCalcSize && message.WParam != IntPtr.Zero)
        {
            var bounds = Marshal.PtrToStructure<NcCalcSizeParams>(message.LParam);
            int frame = FrameThickness();
            bounds.Target.Left += frame;
            bounds.Target.Right -= frame;
            bounds.Target.Bottom -= frame;
            if (form.WindowState == FormWindowState.Maximized)
            {
                bounds.Target.Top += frame;
            }

            Marshal.StructureToPtr(bounds, message.LParam, fDeleteOld: false);
            message.Result = IntPtr.Zero;
            return true;
        }

        if (message.Msg == WmNcActivate)
        {
            message.Result = (IntPtr)1;
            form.Invalidate();
            return true;
        }

        if (message.Msg != WmNcHitTest)
        {
            return false;
        }

        NativeWndProc(form.Handle, ref message);
        if (message.Result != (IntPtr)HtClient)
        {
            return true;
        }

        Point client = form.PointToClient(PointFromLParam(message.LParam));
        CaptionRegion caption = ribbon.HitCaption(form, client);
        if (caption is CaptionRegion.Minimize or CaptionRegion.Maximize or CaptionRegion.Close)
        {
            message.Result = (IntPtr)HtClient;
            return true;
        }

        bool maximized = form.WindowState == FormWindowState.Maximized;
        bool top = !maximized && client.Y <= ResizeGrip;
        bool left = client.X <= ResizeGrip;
        bool right = client.X >= form.ClientSize.Width - ResizeGrip;
        bool bottom = client.Y >= form.ClientSize.Height - ResizeGrip;
        if (top && left)
        {
            message.Result = (IntPtr)HtTopLeft;
            return true;
        }
        if (top && right)
        {
            message.Result = (IntPtr)HtTopRight;
            return true;
        }
        if (bottom && left)
        {
            message.Result = (IntPtr)HtBottomLeft;
            return true;
        }
        if (bottom && right)
        {
            message.Result = (IntPtr)HtBottomRight;
            return true;
        }
        if (top)
        {
            message.Result = (IntPtr)HtTop;
            return true;
        }
        if (left)
        {
            message.Result = (IntPtr)HtLeft;
            return true;
        }
        if (right)
        {
            message.Result = (IntPtr)HtRight;
            return true;
        }
        if (bottom)
        {
            message.Result = (IntPtr)HtBottom;
            return true;
        }

        // HTCAPTION is the WinForms equivalent of WPF WindowChrome:
        // Windows owns move, Aero Snap, drag-to-restore, and caption double-click.
        message.Result = caption switch
        {
            CaptionRegion.Drag => (IntPtr)HtCaption,
            CaptionRegion.SystemMenu => (IntPtr)HtSysMenu,
            _ => (IntPtr)HtClient,
        };
        return true;
    }

    public static void NotifyChanged(Form form)
    {
        if (!form.IsHandleCreated)
        {
            return;
        }

        SetWindowPos(
            form.Handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder);
    }

    private static int FrameThickness() =>
        GetSystemMetrics(SmCxFrame) + GetSystemMetrics(SmCxPaddedBorder);

    private static Point PointFromLParam(IntPtr value)
    {
        int packed = unchecked((int)(long)value);
        return new Point(unchecked((short)packed), unchecked((short)(packed >> 16)));
    }

    private static void NativeWndProc(IntPtr handle, ref Message message) =>
        message.Result = DefWindowProc(handle, message.Msg, message.WParam, message.LParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NcCalcSizeParams
    {
        public Rect Target;
        public Rect Source;
        public Rect OldSource;
        public IntPtr Position;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
}
