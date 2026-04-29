namespace OmniParser;

using Common.Extensions;
using System.Diagnostics;
using System.Drawing;

internal class Program
{
    static void Main(string[] args)
    {
        using var bitmap = new Bitmap("Resources/omni/test4.png");
        using var engine = new OmniParserEngine();

        var image = bitmap.ToBytes(out var imageShape);
        var boxes = engine.Detect(image, imageShape);

        foreach (var box in boxes)
        {
            Console.WriteLine($"{box.Score:F4}\t{box.Box,-40}");
        }

        bitmap.DrawBoxes(boxes.Select(x => (x.Box, x.Score)));
        bitmap.Show("boxes", false);

        Benchmark("Detect", () =>
        {
            _ = engine.Detect(image, imageShape);
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