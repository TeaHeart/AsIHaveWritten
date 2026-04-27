namespace AsIHaveWritten.Helpers;

using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

internal static class WindowHelper
{
    internal static bool SetProcessDPIAware() => PInvoke.SetProcessDPIAware();
    internal static bool GetCursorPos(out Point point) => PInvoke.GetCursorPos(out point);

    internal static bool ShowWindow(IntPtr handle, bool setForeground)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var hWnd = (HWND)handle;

        if (!PInvoke.ShowWindow(hWnd, SHOW_WINDOW_CMD.SW_RESTORE))
        {
            return false;
        }

        if (setForeground && !PInvoke.SetForegroundWindow(hWnd))
        {
            return false;
        }

        return true;
    }
}
