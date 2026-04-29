namespace OmniParser;

using Microsoft.ML.OnnxRuntime;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

internal readonly record struct PreprocessResult(OrtValue Input,
                                                 OrtValue Output,
                                                 float[] OutputBuffer,
                                                 float PadW,
                                                 float PadH,
                                                 float Scale,
                                                 Size OriginalSize) : IDisposable
{
    public void Dispose()
    {
        Input.Dispose();
        Output.Dispose();
    }
}

internal static class Detector
{
    private const int InputSize = 640;
    private const float ConfThreshold = 0.05f;
    private const float NmsThreshold = 0.5f;

    private static readonly long[] OutputShape = [1, 5, 8400];
    private static readonly int OutputLength = 5 * 8400;

    public static PreprocessResult Preprocess(ReadOnlySpan<byte> image, ReadOnlySpan<long> imageShape)
    {
        if (imageShape.Length < 3)
        {
            throw new ArgumentException("imageShape must contain height, width, and channels.", nameof(imageShape));
        }

        var height = checked((int)imageShape[0]);
        var width = checked((int)imageShape[1]);
        var channels = checked((int)imageShape[2]);
        if (channels < 3)
        {
            throw new ArgumentException("YOLO detection requires at least 3 image channels.", nameof(imageShape));
        }

        var (padW, padH, scale) = ComputeLetterbox(width, height, InputSize, InputSize);
        var inputShape = new long[] { 1, 3, InputSize, InputSize };
        var inputArray = new float[InputSize * InputSize * 3];
        FillLetterboxInput(image, width, height, channels, inputArray, padW, padH, scale);

        var outputBuffer = new float[OutputLength];
        var inputOrt = OrtValue.CreateTensorValueFromMemory(inputArray, inputShape);

        try
        {
            var outputOrt = OrtValue.CreateTensorValueFromMemory(outputBuffer, OutputShape);

            return new(inputOrt,
                       outputOrt,
                       outputBuffer,
                       padW,
                       padH,
                       scale,
                       new(width, height));
        }
        catch
        {
            inputOrt.Dispose();
            throw;
        }
    }

    public static IReadOnlyList<(Rectangle Box, float Score)> Postprocess(ReadOnlySpan<float> output, PreprocessResult input)
    {
        var numDetections = 8400;
        var candidates = new List<(Rectangle Box, float Score)>(numDetections);

        for (int i = 0; i < numDetections; i++)
        {
            var cx = output[0 * numDetections + i];
            var cy = output[1 * numDetections + i];
            var w = output[2 * numDetections + i];
            var h = output[3 * numDetections + i];
            var conf = output[4 * numDetections + i];

            if (conf < ConfThreshold)
            {
                continue;
            }

            var x1 = (cx - w / 2 - input.PadW) / input.Scale;
            var y1 = (cy - h / 2 - input.PadH) / input.Scale;
            var x2 = (cx + w / 2 - input.PadW) / input.Scale;
            var y2 = (cy + h / 2 - input.PadH) / input.Scale;

            x1 = Math.Clamp(x1, 0, input.OriginalSize.Width);
            y1 = Math.Clamp(y1, 0, input.OriginalSize.Height);
            x2 = Math.Clamp(x2, 0, input.OriginalSize.Width);
            y2 = Math.Clamp(y2, 0, input.OriginalSize.Height);

            candidates.Add((Rectangle.FromLTRB((int)x1, (int)y1, (int)x2, (int)y2), conf));
        }

        return NonMaxSuppression(candidates, NmsThreshold);
    }

    private static (float PadW, float PadH, float Scale) ComputeLetterbox(int sourceWidth,
                                                                          int sourceHeight,
                                                                          int targetWidth,
                                                                          int targetHeight)
    {
        var scale = Math.Min((float)targetWidth / sourceWidth, (float)targetHeight / sourceHeight);
        var resizedWidth = (int)(sourceWidth * scale);
        var resizedHeight = (int)(sourceHeight * scale);
        var padW = (targetWidth - resizedWidth) / 2f;
        var padH = (targetHeight - resizedHeight) / 2f;
        return (padW, padH, scale);
    }

    private static void FillLetterboxInput(ReadOnlySpan<byte> image,
                                           int sourceWidth,
                                           int sourceHeight,
                                           int channels,
                                           Span<float> input,
                                           float padW,
                                           float padH,
                                           float scale)
    {
        const byte padColor = 114;
        var expectedLength = checked(sourceWidth * sourceHeight * channels);
        if (image.Length < expectedLength)
        {
            throw new ArgumentException("image buffer is smaller than imageShape requires.", nameof(image));
        }

        using var source = new Bitmap(sourceWidth, sourceHeight, PixelFormat.Format24bppRgb);
        CopyPackedRgbBytesToBitmap(source, image, sourceWidth, sourceHeight, channels);

        using var letterbox = new Bitmap(InputSize, InputSize, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(letterbox))
        {
            graphics.Clear(Color.FromArgb(padColor, padColor, padColor));
            graphics.DrawImage(source, (int)padW, (int)padH, (int)(sourceWidth * scale), (int)(sourceHeight * scale));
        }

        CopyBitmapToNchwInput(letterbox, input);
    }

    private static void CopyPackedRgbBytesToBitmap(Bitmap bitmap,
                                                   ReadOnlySpan<byte> image,
                                                   int width,
                                                   int height,
                                                   int channels)
    {
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb);

        try
        {
            var rowLength = checked(width * 3);
            var sourceRowLength = checked(width * channels);
            var rowBuffer = new byte[rowLength];
            for (int y = 0; y < height; y++)
            {
                var sourceRow = image.Slice(y * sourceRowLength, sourceRowLength);
                sourceRow.Slice(0, rowLength).CopyTo(rowBuffer);
                Marshal.Copy(rowBuffer, 0, bitmapData.Scan0 + y * bitmapData.Stride, rowLength);
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private static void CopyBitmapToNchwInput(Bitmap bitmap, Span<float> input)
    {
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, InputSize, InputSize),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);

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
                    var targetOffset = y * InputSize + x;
                    input[0 * InputSize * InputSize + targetOffset] = rowBytes[pixelOffset + 0] / 255f;
                    input[1 * InputSize * InputSize + targetOffset] = rowBytes[pixelOffset + 1] / 255f;
                    input[2 * InputSize * InputSize + targetOffset] = rowBytes[pixelOffset + 2] / 255f;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private static List<(Rectangle Box, float Score)> NonMaxSuppression(List<(Rectangle Box, float Score)> boxes, float iouThreshold)
    {
        if (boxes.Count == 0)
        {
            return [];
        }

        boxes.Sort((a, b) => b.Score.CompareTo(a.Score));
        var result = new List<(Rectangle Box, float Score)>(boxes.Count);
        var suppressed = new bool[boxes.Count];

        for (int i = 0; i < boxes.Count; i++)
        {
            if (suppressed[i])
            {
                continue;
            }

            result.Add(boxes[i]);

            for (int j = i + 1; j < boxes.Count; j++)
            {
                if (suppressed[j])
                {
                    continue;
                }

                if (IoU(boxes[i].Box, boxes[j].Box) > iouThreshold)
                {
                    suppressed[j] = true;
                }
            }
        }

        return result;
    }

    private static float IoU(Rectangle a, Rectangle b)
    {
        var inter = Rectangle.Intersect(a, b);
        if (inter.Width <= 0 || inter.Height <= 0)
        {
            return 0;
        }

        var interArea = inter.Width * inter.Height;
        var unionArea = a.Width * a.Height + b.Width * b.Height - interArea;
        return unionArea > 0 ? (float)interArea / unionArea : 0;
    }
}