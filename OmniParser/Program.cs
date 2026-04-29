namespace OmniParser;

using System.Diagnostics;
using System.Drawing;

internal class Program
{
    static void Main(string[] args)
    {
        var imgDir = Path.Combine("Resources", "imgs");
        var outDir = Directory.CreateDirectory(Path.Combine(imgDir, "out"));

        var engine = new OmniParserEngine();

        foreach (var file in Directory.EnumerateFiles(imgDir).Where(x=>x.EndsWith(".png") || x.EndsWith(".jpg")))
        {
            Console.WriteLine($"\n=== {Path.GetFileName(file)} ===");
            using var bmp = new Bitmap(file);
            var result = engine.ParseScreen(bmp);

            foreach (var e in result.Elements)
            {
                var content = e.OcrContent ?? e.FlorenceContent ?? "(none)";
                Console.WriteLine($"  [{e.Type}] {e.Box.X},{e.Box.Y} {e.Box.Width}x{e.Box.Height} | {content}");
            }

            var outPath = Path.Combine(outDir.FullName, Path.GetFileName(file));
            result.AnnotatedImage.Save(outPath);
            Console.WriteLine($"  -> saved: {outPath}");

            result.AnnotatedImage.Dispose();
        }
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
