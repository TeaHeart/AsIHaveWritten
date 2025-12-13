namespace AsIHaveWritten.Helpers;

using OpenCvSharp;

public class ImageHelper
{
    public static Mat CalcHist(Mat image, int[]? channels = null, int[]? histSize = null, Rangef[]? ranges = null)
    {
        channels ??= [0, 1, 2];
        histSize ??= [32, 32, 32];
        ranges ??= [new(0, 256), new(0, 256), new(0, 256)];

        var hist = new Mat();
        Cv2.CalcHist([image], channels, null, hist, 3, histSize, ranges);
        return hist;
    }
}