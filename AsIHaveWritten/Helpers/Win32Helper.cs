namespace AsIHaveWritten.Helpers;

using System.Diagnostics;
using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.Xps;
using Windows.Win32.UI.WindowsAndMessaging;

public static class Win32Helper
{
    public static bool SetProcessDPIAware() => PInvoke.SetProcessDPIAware();
    public static bool IsForegroundWindow(IntPtr hWnd) => hWnd != IntPtr.Zero && hWnd == PInvoke.GetForegroundWindow();

    public static IntPtr GetWindowHandle(string processName)
    {
        ArgumentNullException.ThrowIfNull(processName, nameof(processName));
        return Process.GetProcessesByName(processName)
                      .Select(x => x.MainWindowHandle)
                      .FirstOrDefault(x => x != IntPtr.Zero);
    }

    public static Point? GetCursorPos()
    {
        if (!PInvoke.GetCursorPos(out var point))
        {
            return null;
        }

        return point;
    }

    public static Rectangle? GetClientRectangle(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var hWnd = (HWND)handle;

        if (!PInvoke.GetClientRect(hWnd, out var lpRect))
        {
            return null;
        }

        var point = new Point();
        if (!PInvoke.ClientToScreen(hWnd, ref point))
        {
            return null;
        }

        return new(point, lpRect.Size);
    }

    public static bool ShowWindow(IntPtr handle, bool setForeground)
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

    public static Bitmap? PrintWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var hWnd = (HWND)handle;

        if (PInvoke.IsIconic(hWnd))
        {
            return null;
        }

        if (!PInvoke.GetClientRect(hWnd, out var rect))
        {
            return null;
        }

        var bitmap = new Bitmap(rect.Width, rect.Height);
        using var graph = Graphics.FromImage(bitmap);
        var hdc = graph.GetHdc();
        var isSucceeds = PInvoke.PrintWindow(hWnd, (HDC)hdc, PRINT_WINDOW_FLAGS.PW_CLIENTONLY | (PRINT_WINDOW_FLAGS)2U); // PW_RENDERFULLCONTENT
        graph.ReleaseHdc(hdc);

        if (!isSucceeds)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }
}
