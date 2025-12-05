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
        // var winH = new WindowHelper("StarRail");
        var det = new Detector(@"Resources\models\PP-OCRv5_mobile_det_infer.onnx");
        using var rec = new Recognizer(@"Resources\models\PP-OCRv5_mobile_rec_infer.onnx", @"Resources\models\characterDict.txt");
        using var image = new Bitmap(@"Resources\4.png");

        for (int i = 0; i < 1000; i++)
        {
            var s = Stopwatch.StartNew();

            var result = det.Detect(image);
            var rois = result.Select(x => x.Box).ToArray();
            var result2 = rec.Recognize(image, rois);

            Console.WriteLine($"----\t{s.ElapsedMilliseconds}\t-------------------------------------------------");
            Array.ForEach(result2,x => Console.WriteLine($"{x.Score*100,6:F}, '{x.Text}'"));
        }


        //image.DrawAndShow(rois);
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

    public static void DrawAndShow(this Bitmap image, Rect[] result)
    {
        using var g = Graphics.FromImage(image);
        using var p = new Pen(Color.Red);
        if (result.Length != 0)
        {
            g.DrawRectangles(p, result.Select(x => new Rectangle(x.X, x.Y, x.Width, x.Height)).ToArray());

        }
        image.Show();
    }
    public static void Show(this Bitmap img, string title = "image")
    {
        using var mat = img.ToMat();
        mat.Show();
    }

    public static void Show(this Mat mat, string title = "image", bool wait = true)
    {
        Cv2.ImShow(title, mat);
        if (wait)
        {
            Cv2.WaitKey();
        }
    }
}
