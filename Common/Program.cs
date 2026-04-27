namespace Common;

using Common.Extensions;
using Common.Helpers;
using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;

internal class Program
{
    static void Main(string[] args)
    {
        using var wm = new WindowMonitor("StarRail");
        var handle = wm.Handle;
        var clientBounds = wm.ClientBounds;
        var isForeground = wm.IsForeground;
        using var bitmap = wm.Screenshot;

        Console.WriteLine((handle, clientBounds, isForeground));
        bitmap?.Show("bitmap", false);

        Benchmark("WindowMonitor", () =>
        {
            _ = wm.Handle;
            _ = wm.ClientBounds;
            _ = wm.IsForeground;
            using var _1 = wm.Screenshot;
        });

        Benchmark("CopyFromScreen", () =>
        {
            WindowHelper.CopyFromScreen(clientBounds, out var bitmap);
            bitmap?.Dispose();
        });

        Benchmark("PrintWindow", () =>
        {
            WindowHelper.PrintWindow(handle, clientBounds.Size, out var bitmap);
            bitmap?.Dispose();
        });
    }

    static void GlobalHookDemo()
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

            if (msg.message == 0x0312)
            {
                Console.WriteLine("Hotkey pressed!");
                Console.WriteLine(msg.time);
            }
        }

        PInvoke.UnregisterHotKey(HWND.Null, 1);
    }

    static void Benchmark(string title, Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var times = new List<long>();
        var sw = new Stopwatch();
        var totalTicks = 0L;

        while (totalTicks < TimeSpan.TicksPerSecond)
        {
            sw.Restart();
            action();
            sw.Stop();
            totalTicks += sw.ElapsedTicks;
            times.Add(sw.ElapsedTicks);
        }

        var min = (double)times.Min() / TimeSpan.TicksPerMillisecond;
        var max = (double)times.Max() / TimeSpan.TicksPerMillisecond;
        var avg = (double)times.Average() / TimeSpan.TicksPerMillisecond;
        var sum = (double)times.Sum() / TimeSpan.TicksPerMillisecond;
        Console.WriteLine($"{title,20}\t{times.Count}\tmin:{min:F4}ms\tmax:{max:F4}ms\tavg:{avg:F4}ms\tsum:{sum:F4}ms");
    }
}
