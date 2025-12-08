namespace AsIHaveWritten;

using AsIHaveWritten.Helpers;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Diagnostics;
using System.Drawing;
using System.Text.Encodings.Web;
using System.Text.Json;

internal class Program
{
    static void Main(string[] args)
    {
        var win = new WindowHelper("StarRail");
        using var det = new Detector(@"Resources\models\PP-OCRv5_mobile_det_infer.onnx");
        using var rec = new Recognizer(@"Resources\models\PP-OCRv5_mobile_rec_infer.onnx", @"Resources\models\characterDict.txt");

        win.SetVisible(true);
        while (true)
        {
            try
            {
                var s = Stopwatch.StartNew();
                win.UpdateClientRect();
                using var image = win.GetImage();

                var boxes = det.Detect(image);
                var rois = boxes.Select(x => x.Box).ToArray();

                if (rois.Length != 0)
                {
                    var result = rec.Recognize(image, rois);
                    Array.ForEach(result, x => Console.WriteLine($"{x.Score * 100,6:F}%, '{x.Text}', [{x.Box.X}, {x.Box.Y}, {x.Box.Width}, {x.Box.Height}]"));
                }

                Console.WriteLine($"----\t{s.ElapsedMilliseconds} ms\t----");

                image.DrawAndShow(rois, "win", false, factor: 0.5f);
                //image.Show("win", false, factor: 0.5f);
                Cv2.WaitKey(1);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
            }
        }
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

    public static void DrawAndShow(this Bitmap image, Rect[] result, string title = "image", bool wait = true, float factor = 1)
    {
        using var g = Graphics.FromImage(image);
        using var p = new Pen(Color.Red);
        if (result.Length != 0)
        {
            g.DrawRectangles(p, result.Select(x => new Rectangle(x.X, x.Y, x.Width, x.Height)).ToArray());

        }
        image.Show(title, wait, factor);
    }
    public static void Show(this Bitmap img, string title = "image", bool wait = true, float factor = 1)
    {
        using var mat = img.ToMat();
        mat.Show(title, wait, factor);
    }

    public static void Show(this Mat mat, string title = "image", bool wait = true, float factor = 1)
    {
        using var resized = new Mat();
        Cv2.Resize(mat, resized, new(mat.Width * factor, mat.Height * factor));
        Cv2.ImShow(title, resized);
        if (wait)
        {
            Cv2.WaitKey();
        }
    }
}
