namespace PaddleOcr;

using PaddleOcr.Extensions;
using System.Diagnostics;
using System.Drawing;

internal class Program
{
    static void Main(string[] args)
    {
        using var bitmap = new Bitmap("Resources/ppocr/test4.png");

        using var engine = new PaddleOcrEngine();

        var image = bitmap.ToBytes(out var imageShape);
        var regions = new[] { bitmap.GetBounds() };

        var boxes = engine.Detect(image, imageShape, regions);
        var allTextRegion = boxes.Select(x => x.Box).ToArray();
        var texts = engine.Recognize(image, imageShape, allTextRegion);
        var result = engine.DetectAndRecognize(image, imageShape, regions);

        foreach (var (Det, Rec) in result)
        {
            Console.WriteLine($"{Det.Score:F4}\t{Det.Box,-40}\t{Rec.Score:F4}\t{Rec.Text}");
        }

        bitmap.DrawBoxes(boxes);
        bitmap.Show("boxes", false);

        Benchmark("Detect", () =>
        {
            var boxes = engine.Detect(image, imageShape, regions);
        });

        Benchmark("Recognize", () =>
        {
            var texts = engine.Recognize(image, imageShape, allTextRegion);
        });

        Benchmark("DetectAndRecognize", () =>
        {
            var result = engine.DetectAndRecognize(image, imageShape, regions);
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
