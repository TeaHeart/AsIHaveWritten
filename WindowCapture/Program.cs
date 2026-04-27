namespace WindowCapture;

using System.Diagnostics;
using WindowCapture.Extensions;
using WindowCapture.Helpers;

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
