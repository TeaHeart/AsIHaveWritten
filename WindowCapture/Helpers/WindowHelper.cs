namespace WindowCapture.Helpers;

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.Xps;
using Windows.Win32.UI.WindowsAndMessaging;

internal static class WindowHelper
{
    internal static bool SetProcessDPIAware() => PInvoke.SetProcessDPIAware();
    internal static bool IsForegroundWindow(nint hWnd) => hWnd != nint.Zero && hWnd == PInvoke.GetForegroundWindow();
    internal static nint GetDesktopWindow() => PInvoke.GetDesktopWindow();

    internal static nint GetWindowHandle(string processName)
    {
        var processes = Process.GetProcessesByName(processName);

        try
        {
            foreach (var item in processes)
            {
                if (item.MainWindowHandle != nint.Zero)
                {
                    return item.MainWindowHandle;
                }
            }

            return nint.Zero;
        }
        finally
        {
            foreach (var item in processes)
            {
                item.Dispose();
            }
        }
    }

    internal static bool GetClientBounds(nint handle, out Rectangle clientBounds)
    {
        clientBounds = default;
        if (handle == nint.Zero)
        {
            return false;
        }

        // 获取客户区大小
        var hWnd = (HWND)handle;
        if (!PInvoke.GetClientRect(hWnd, out var rect))
        {
            return false;
        }

        // 对于 BitBlt 需要知道窗口位置
        var point = new Point();
        if (!PInvoke.ClientToScreen(hWnd, ref point))
        {
            return false;
        }

        clientBounds = new(point, rect.Size);
        return true;
    }

    internal static bool SetWindowVisiable(nint handle, bool setForeground = false)
    {
        if (handle == nint.Zero)
        {
            return false;
        }

        var hWnd = (HWND)handle;

        // 最小化获取客户端尺寸会失败，需要先恢复窗口
        if (!PInvoke.ShowWindow(hWnd, SHOW_WINDOW_CMD.SW_RESTORE))
        {
            return false;
        }

        // 对于 BitBlt 窗口要未遮挡
        if (setForeground && !PInvoke.SetForegroundWindow(hWnd))
        {
            return false;
        }

        return true;
    }

    internal static bool PrintWindow(nint hWnd, Size clientSize, [MaybeNullWhen(false)] out Bitmap bitmap)
    {
        if (hWnd == nint.Zero || clientSize.Width * clientSize.Height <= 0)
        {
            bitmap = null;
            return false;
        }

        bitmap = new Bitmap(clientSize.Width, clientSize.Height);

        try
        {
            using var graph = Graphics.FromImage(bitmap);
            // PrintWindow 对透明或是被遮挡的窗口也能工作，对于 hWnd 操作可能需要管理员权限，速度其实也和 BitBlt 差不了太多
            // PW_RENDERFULLCONTENT = 2U
            if (!PInvoke.PrintWindow((HWND)hWnd, (HDC)graph.GetHdc(), PRINT_WINDOW_FLAGS.PW_CLIENTONLY | (PRINT_WINDOW_FLAGS)2U))
            {
                throw new Win32Exception();
            }
            return true;
        }
        catch
        {
            bitmap.Dispose();
            return false;
        }
    }

    internal static bool CopyFromScreen(Rectangle region, [MaybeNullWhen(false)] out Bitmap bitmap)
    {
        if (region.Width * region.Height <= 0)
        {
            bitmap = null;
            return false;
        }

        bitmap = new Bitmap(region.Width, region.Height);

        try
        {
            using var graph = Graphics.FromImage(bitmap);
            // Graphics 底层也是调用 BitBlt ，速度差不多
            // BitBlt 对透明或是被遮挡的窗口不能按预期工作
            graph.CopyFromScreen(region.Location, new(), region.Size);
            return true;
        }
        catch
        {
            bitmap.Dispose();
            return false;
        }
    }

    // internal static bool CopyFromScreenByPInvoke(Rectangle region, [MaybeNullWhen(false)] out Bitmap bitmap)
    // {
    //     if (region.Width * region.Height <= 0)
    //     {
    //         bitmap = null;
    //         return false;
    //     }

    //     var hdcSrc = HDC.Null;
    //     var hdcDest = HDC.Null;
    //     var hBitmap = HBITMAP.Null;
    //     var hOld = HGDIOBJ.Null;

    //     try
    //     {
    //         if (HDC.Null == (hdcSrc = PInvoke.GetDC(HWND.Null))
    //          || HDC.Null == (hdcDest = PInvoke.CreateCompatibleDC(hdcSrc))
    //          || HBITMAP.Null == (hBitmap = PInvoke.CreateCompatibleBitmap(hdcSrc, region.Width, region.Height))
    //          || HGDIOBJ.Null == (hOld = PInvoke.SelectObject(hdcDest, hBitmap))
    //          || !PInvoke.BitBlt(hdcDest, 0, 0, region.Width, region.Height, hdcSrc, region.X, region.Y, ROP_CODE.SRCCOPY)
    //         )
    //         {
    //             bitmap = null;
    //             return false;
    //         }
    //         bitmap = Image.FromHbitmap(hBitmap);
    //         return true;
    //     }
    //     finally
    //     {
    //         PInvoke.SelectObject(hdcDest, hOld);
    //         PInvoke.DeleteObject(hBitmap);
    //         PInvoke.DeleteDC(hdcDest);
    //         PInvoke.ReleaseDC(HWND.Null, hdcSrc);
    //     }
    // }
}
