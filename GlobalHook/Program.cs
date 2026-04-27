namespace GlobalHook;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

internal class Program
{
    static void Main(string[] args)
    {
        if (!PInvoke.RegisterHotKey(HWND.Null, 1, 0, 0x70))
        {
            Console.WriteLine("Failed to register hotkey.");
            return;
        }

        Console.WriteLine("Hotkey registered. Press Ctrl+Alt+F1 to trigger.");

        while (PInvoke.GetMessage(out var msg, HWND.Null, 0, 0))
        {
            PInvoke.TranslateMessage(msg);
            PInvoke.DispatchMessage(msg);
            Console.WriteLine(msg);
            
            if (msg.message == 0x0312)
            {
                Console.WriteLine("Hotkey pressed!");
                Console.WriteLine(msg.time);
            }
        }

        // 注销热键
        PInvoke.UnregisterHotKey(HWND.Null, 1);
    }
}
