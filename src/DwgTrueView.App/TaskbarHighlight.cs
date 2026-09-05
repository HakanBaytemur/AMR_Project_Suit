using System.Runtime.InteropServices;

namespace DwgTrueView.App;

/// <summary>
/// Windows taskbar progress and completion flash, matching Explorer and
/// other desktop apps: green fill while work runs, orange highlight if the
/// window is in the background when it succeeds.
/// </summary>
internal static class TaskbarHighlight
{
    private const uint FlashTrayUntilForeground = 0x0000000E;
    private const uint FlashStop = 0;
    private const int ProgressNone = 0;
    private const int ProgressNormal = 2;
    private const int ProgressError = 4;

    private static ITaskbarList3? _taskbar;

    public static void SetProgress(Form form, int percent)
    {
        if (!TryGetHwnd(form, out IntPtr hwnd) || !TryGetTaskbar(out ITaskbarList3 taskbar))
        {
            return;
        }

        try
        {
            uint value = (uint)Math.Clamp(percent, 0, 100);
            taskbar.SetProgressState(hwnd, ProgressNormal);
            taskbar.SetProgressValue(hwnd, value, 100);
        }
        catch (COMException)
        {
        }
    }

    public static void Clear(Form form)
    {
        if (!TryGetHwnd(form, out IntPtr hwnd) || !TryGetTaskbar(out ITaskbarList3 taskbar))
        {
            return;
        }

        try
        {
            taskbar.SetProgressState(hwnd, ProgressNone);
        }
        catch (COMException)
        {
        }
    }

    public static void NotifySuccess(Form form)
    {
        SetProgress(form, 100);
        Clear(form);
        Flash(form, FlashTrayUntilForeground);
    }

    public static void NotifyError(Form form)
    {
        if (TryGetHwnd(form, out IntPtr hwnd) && TryGetTaskbar(out ITaskbarList3 taskbar))
        {
            try
            {
                taskbar.SetProgressState(hwnd, ProgressError);
                taskbar.SetProgressValue(hwnd, 100, 100);
            }
            catch (COMException)
            {
            }
        }

        Flash(form, FlashTrayUntilForeground);
    }

    public static void StopFlash(Form form)
    {
        Flash(form, FlashStop);
    }

    private static void Flash(Form form, uint flags)
    {
        if (!TryGetHwnd(form, out IntPtr hwnd))
        {
            return;
        }

        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Hwnd = hwnd,
            Flags = flags,
            Count = flags == FlashStop ? 0u : uint.MaxValue,
            Timeout = 0,
        };
        _ = FlashWindowEx(ref info);
    }

    private static bool TryGetHwnd(Form form, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        if (form.IsDisposed || !form.IsHandleCreated)
        {
            return false;
        }

        hwnd = form.Handle;
        return hwnd != IntPtr.Zero;
    }

    private static bool TryGetTaskbar(out ITaskbarList3 taskbar)
    {
        if (_taskbar is not null)
        {
            taskbar = _taskbar;
            return true;
        }

        try
        {
            var created = (ITaskbarList3)new TaskbarList();
            created.HrInit();
            _taskbar = created;
            taskbar = created;
            return true;
        }
        catch (COMException)
        {
            taskbar = null!;
            return false;
        }
        catch (InvalidCastException)
        {
            taskbar = null!;
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarList
    {
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int flags);
    }
}
