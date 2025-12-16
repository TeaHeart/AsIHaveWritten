namespace AsIHaveWritten.Extensions;

using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Drawing;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

public static class Cv2Extensions
{
    public static void Show(this Bitmap image, string title = "image", double factor = 1, int delay = 0)
    {
        using var mat = image.ToMat();
        mat.Show(title, factor, delay);
    }

    public static void Show(this Mat image, string title = "image", double factor = 1, int delay = 0)
    {
        using var resized = new Mat();
        Cv2.Resize(image, resized, new(image.Width * factor, image.Height * factor));
        Cv2.ImShow(title, resized);
        Cv2.WaitKey(delay);
    }

    public static Mat CalcHist(this Mat image, int[]? channels = null, int[]? histSize = null, Rangef[]? ranges = null)
    {
        channels ??= [0, 1, 2];
        histSize ??= [32, 32, 32];
        ranges ??= [new(0, 256), new(0, 256), new(0, 256)];

        var hist = new Mat();
        Cv2.CalcHist([image], channels, null, hist, 3, histSize, ranges);
        Cv2.Normalize(hist, hist, 0, 1, NormTypes.MinMax);
        return hist;
    }

    public static double CompareByHist(this Mat selfHist, Mat otherHist)
    {
        return Cv2.CompareHist(selfHist, otherHist, HistCompMethods.Correl);
    }

    public static Point ToDrawing(this CvPoint src) => new(src.X, src.Y);
    public static Size ToDrawing(this CvSize src) => new(src.Width, src.Height);
    public static Rectangle ToDrawing(this Rect src) => new(src.X, src.Y, src.Width, src.Height);
    public static CvPoint ToCv(this Point src) => new(src.X, src.Y);
    public static CvSize ToCv(this Size src) => new(src.Width, src.Height);
    public static Rect ToCv(this Rectangle src) => new(src.X, src.Y, src.Width, src.Height);
}
