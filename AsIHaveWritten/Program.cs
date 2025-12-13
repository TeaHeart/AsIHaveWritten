namespace AsIHaveWritten;

using AsIHaveWritten.Helpers;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.Serialization;
using System.Text.Encodings.Web;
using System.Text.Json;

internal class Program
{
    static void Main(string[] args)
    {
        var win = new WindowHelper("StarRail");
        win.SetVisible(true);
        using var det = new Detector(@"Resources\models\PP-OCRv5_mobile_det_infer.onnx");
        using var rec = new Recognizer(@"Resources\models\PP-OCRv5_mobile_rec_infer.onnx", @"Resources\models\characterDict.txt");

        var router = new List<(string Name, Mat Hist, Action Function)>();

        void AddRoute(string name, Action function)
        {
            using var mat = Cv2.ImRead(Path.Combine(@"Resources\货币战争", $"{name}.png"));
            var hist = ImageHelper.CalcHist(mat);
            router.Add((name, hist, function));
        }

        void LeftClick(int x, int y)
        {
            win.LeftClick(x / 1920f, y / 1080f);
            Thread.Sleep(100);
        }

        Rect GetRect(int x, int y, int w, int h)
        {
            var (nx, xy, nw, nh) = win.ToAbsolute(x / 1920f, y / 1080f, w / 1920f, h / 1080f, false);
            return new(nx, xy, nw, nh);
        }

        var ctx = new Dictionary<string, object>();

        AddRoute("001开始界面", () =>
        {
            ctx.Clear();
            LeftClick(1587, 969);
        });
        AddRoute("002模式选择", () => LeftClick(1636, 960));
        AddRoute("003等级选择", () => LeftClick(1670, 974));
        AddRoute("004对手信息", () =>
        {
            var nextStep = GetRect(1469, 968, 90, 35);
            var tags = GetRect(40, 960, 888, 50);
            var mat = (Mat)ctx["mat"];

            var result = rec.Recognize(mat, [nextStep, tags]);
            if (result[0].Text == null || !result[0].Text.Contains("下一步"))
            {
                return;
            }

            ctx["tags"] = result[1].Text;
            Console.WriteLine($"敌人 tags: {result[1].Text}");
            LeftClick(1511, 986);
        });
        AddRoute("005位面信息", () => LeftClick(960, 960));
        AddRoute("006投资环境", () =>
        {
            var confrim = GetRect(1049, 968, 63, 31);
            var remain = GetRect(700, 960, 155, 48);

            var cv1 = GetRect(234, 375, 420, 220);
            var cv2 = GetRect(750, 375, 420, 220);
            var cv3 = GetRect(1273, 375, 420, 220);
            var mat = (Mat)ctx["mat"];

            var result = rec.Recognize(mat, [confrim, remain]);
            if (result[0].Text == null || !result[0].Text.Contains("确认"))
            {
                return;
            }
            var hasMore = result[1].Text?.Contains('1') ?? false;

            foreach (var cv in new[] { cv1, cv2, cv3 })
            {
                using var tmp = mat[cv];
                var rois = det.Detect(tmp).Select(x => x.Box).ToArray();
                if (rois.Length != 0)
                {
                    var rs = rec.Recognize(tmp, rois).Select(x => x.Text);
                    var text = string.Join("", rs);
                    if (text.Contains("佩佩"))
                    {
                        var (sx,sy) = ((float)cv.X / win._windowRect.Width * 1920f, (float)cv.Y / win._windowRect.Height * 1080f);
                        LeftClick((int)sx, (int)sy);
                        LeftClick(1049, 968);
                        ctx["佩佩"] = true;
                        return;
                    }
                }
            }
            if (hasMore)
            {
                LeftClick(674, 984);
            }
            else
            {
                LeftClick(750, 375);
                LeftClick(1049, 968);
                ctx["#restart_flag"] = true;
            }
        });
        AddRoute("007备战阶段", () =>
        {
            if (ctx.GetValueOrDefault("#restart_flag", false) is bool b && b == true)
            {
                LeftClick(62, 62);
            }
            if (ctx.GetValueOrDefault("佩佩", false) is bool b1 && b1 == true)
            {
                Console.WriteLine("刷到单佩佩，停止运行");
                ctx["#stop_flag"] = true;
            }
        });
        AddRoute("008放弃挑战", () =>
        {
            if (ctx.GetValueOrDefault("#restart_flag", false) is bool b && b == true)
            {
                LeftClick(762, 744);
            }
        });
        AddRoute("009挑战失败", () =>
        {
            LeftClick(963, 895);
        });
        AddRoute("010对局结算1", () =>
        {
            LeftClick(960, 900);
        });
        AddRoute("011对局结算2", () =>
        {
            LeftClick(960, 900);
        });

        while (ctx.GetValueOrDefault("#stop_flag", false) is bool b && b != true)
        {
            var sw = Stopwatch.StartNew();

            win.UpdateClientRect();
            using var img = win.GetImage();
            using var mat = img.ToMat();
            using var hist = ImageHelper.CalcHist(mat);

            ctx["mat"] = mat;
            // 页面判断
            var page = router
               .Select(x => (x.Name, Score: Cv2.CompareHist(x.Hist, hist, HistCompMethods.Correl), x.Function))
               .MaxBy(x => x.Score);

            if (page.Score > 0.9)
            {
                Console.WriteLine($"{sw.ElapsedMilliseconds} ms, {page.Score:F2}, {page.Name}");
                page.Function();
            }

            mat.Show("obs", 0.5f, 1);

            var remain = 1000 - (int)sw.ElapsedMilliseconds;
            if (remain > 0)
            {
                Thread.Sleep(remain);
            }
        }

        router.ForEach(r => r.Hist.Dispose());
        Console.WriteLine("程序停止");
    }
}

public static class Ex
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        // 换行
        WriteIndented = true,
        // 尾随逗号
        AllowTrailingCommas = true,
        // 跳过注释
        ReadCommentHandling = JsonCommentHandling.Skip,
        // 允许的编码器
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToJson(this object obj)
    {
        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    public static void DrawAndShow(this Bitmap image, Rect[] result, string title = "image", float factor = 1, int delay = 0)
    {
        using var g = Graphics.FromImage(image);
        using var p = new Pen(Color.Red);
        if (result.Length != 0)
        {
            g.DrawRectangles(p, result.Select(x => new Rectangle(x.X, x.Y, x.Width, x.Height)).ToArray());
        }
        image.Show(title, factor, delay);
    }

    public static void Show(this Bitmap img, string title = "image", float factor = 1, int delay = 0)
    {
        using var mat = img.ToMat();
        mat.Show(title, factor, delay);
    }

    public static void Show(this Mat mat, string title = "image", float factor = 1, int delay = 0)
    {
        using var resized = new Mat();
        Cv2.Resize(mat, resized, new(mat.Width * factor, mat.Height * factor));
        Cv2.ImShow(title, resized);
        Cv2.WaitKey(delay);
    }
}
