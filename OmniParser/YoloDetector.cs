namespace OmniParser;

using Microsoft.ML.OnnxRuntime;
using System.Drawing;
using System.Runtime.InteropServices;

internal static class YoloDetector
{
    private const int InputSize = 640;
    private const float ConfThreshold = 0.05f;
    private const float NmsThreshold = 0.5f;

    // YOLO 固定输出 [1, 5, 8400]
    private static readonly long[] OutputShape = [1, 5, 8400];
    private static readonly int OutputLength = 5 * 8400;

    public readonly record struct DetectedBox(Rectangle Box, float Confidence);

    /// <summary>
    /// 预处理截图：letterbox → NCHW float32，预分配输入输出 OrtValue。
    /// </summary>
    public static void Preprocess(Bitmap source, out Bitmap resizedImage,
                                  out OrtValue inputOrt, out OrtValue outputOrt,
                                  out float[] outputBuffer)
    {
        var (letterboxImg, padW, padH, scale) = LetterboxResize(source, InputSize, InputSize);

        // 输入 [1,3,640,640]
        var inputShape = new long[] { 1, 3, InputSize, InputSize };
        var inputArray = new float[InputSize * InputSize * 3];

        var bitmapData = letterboxImg.LockBits(
            new Rectangle(0, 0, InputSize, InputSize),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        try
        {
            var stride = bitmapData.Stride;
            var rowBytes = new byte[stride];
            for (int y = 0; y < InputSize; y++)
            {
                Marshal.Copy(bitmapData.Scan0 + y * stride, rowBytes, 0, stride);
                for (int x = 0; x < InputSize; x++)
                {
                    var pixelOffset = x * 3;
                    inputArray[0 * InputSize * InputSize + y * InputSize + x] = rowBytes[pixelOffset + 0] / 255f; // B
                    inputArray[1 * InputSize * InputSize + y * InputSize + x] = rowBytes[pixelOffset + 1] / 255f; // G
                    inputArray[2 * InputSize * InputSize + y * InputSize + x] = rowBytes[pixelOffset + 2] / 255f; // R
                }
            }
        }
        finally
        {
            letterboxImg.UnlockBits(bitmapData);
        }

        outputBuffer = new float[OutputLength];
        inputOrt = OrtValue.CreateTensorValueFromMemory(inputArray, inputShape);
        outputOrt = OrtValue.CreateTensorValueFromMemory(outputBuffer, OutputShape);
        resizedImage = letterboxImg;
        resizedImage.Tag = new LetterboxInfo(padW, padH, scale);
    }

    /// <summary>
    /// 后处理：从预分配的输出 buffer 解析框 → 阈值过滤 → NMS → 原图坐标。
    /// </summary>
    public static List<DetectedBox> Postprocess(
        ReadOnlySpan<float> output,
        Bitmap resizedImage,
        Size originalSize)
    {
        var info = resizedImage.Tag as LetterboxInfo;
        var (padW, padH, scale) = info is not null
            ? (info.PadW, info.PadH, info.Scale)
            : (0f, 0f, 1f);

        var numDetections = 8400;
        var candidates = new List<DetectedBox>(numDetections);

        for (int i = 0; i < numDetections; i++)
        {
            var cx = output[0 * numDetections + i];
            var cy = output[1 * numDetections + i];
            var w = output[2 * numDetections + i];
            var h = output[3 * numDetections + i];
            var conf = output[4 * numDetections + i];

            if (conf < ConfThreshold)
                continue;

            var x1 = (cx - w / 2 - padW) / scale;
            var y1 = (cy - h / 2 - padH) / scale;
            var x2 = (cx + w / 2 - padW) / scale;
            var y2 = (cy + h / 2 - padH) / scale;

            x1 = Math.Clamp(x1, 0, originalSize.Width);
            y1 = Math.Clamp(y1, 0, originalSize.Height);
            x2 = Math.Clamp(x2, 0, originalSize.Width);
            y2 = Math.Clamp(y2, 0, originalSize.Height);

            candidates.Add(new DetectedBox(
                Rectangle.FromLTRB((int)x1, (int)y1, (int)x2, (int)y2),
                conf
            ));
        }

        return NonMaxSuppression(candidates, NmsThreshold);
    }

    private static (Bitmap Resized, float PadW, float PadH, float Scale) LetterboxResize(
        Bitmap source, int targetW, int targetH, byte padColor = 114)
    {
        var srcW = source.Width;
        var srcH = source.Height;
        var scale = Math.Min((float)targetW / srcW, (float)targetH / srcH);
        var newW = (int)(srcW * scale);
        var newH = (int)(srcH * scale);
        var padW = (targetW - newW) / 2f;
        var padH = (targetH - newH) / 2f;

        var resized = new Bitmap(targetW, targetH, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(resized);
        g.Clear(Color.FromArgb(padColor, padColor, padColor));
        g.DrawImage(source, (int)padW, (int)padH, newW, newH);

        return (resized, padW, padH, scale);
    }

    private static List<DetectedBox> NonMaxSuppression(List<DetectedBox> boxes, float iouThreshold)
    {
        if (boxes.Count == 0) return [];

        boxes.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        var result = new List<DetectedBox>(boxes.Count);
        var suppressed = new bool[boxes.Count];

        for (int i = 0; i < boxes.Count; i++)
        {
            if (suppressed[i]) continue;
            result.Add(boxes[i]);

            for (int j = i + 1; j < boxes.Count; j++)
            {
                if (suppressed[j]) continue;
                if (IoU(boxes[i].Box, boxes[j].Box) > iouThreshold)
                    suppressed[j] = true;
            }
        }

        return result;
    }

    private static float IoU(Rectangle a, Rectangle b)
    {
        var inter = Rectangle.Intersect(a, b);
        if (inter.Width <= 0 || inter.Height <= 0) return 0;
        var interArea = inter.Width * inter.Height;
        var unionArea = a.Width * a.Height + b.Width * b.Height - interArea;
        return unionArea > 0 ? (float)interArea / unionArea : 0;
    }

    private sealed record LetterboxInfo(float PadW, float PadH, float Scale);
}
