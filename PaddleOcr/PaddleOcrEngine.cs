namespace PaddleOcr;

using Common.Extensions;
using Microsoft.ML.OnnxRuntime;
using System.Drawing;

public sealed class PaddleOcrEngine : IDisposable
{
    private readonly InferenceSession _det;
    private readonly InferenceSession _rec;
    private readonly string[] _dict;

    public PaddleOcrEngine(string detModelPath = "Resources/ppocr/PP-OCRv5_mobile_det_infer.onnx",
                           string recModelPath = "Resources/ppocr/PP-OCRv5_mobile_rec_infer.onnx",
                           string wordDictPath = "Resources/ppocr/characterDict.txt",
                           SessionOptions? options = null)
    {
        if (options == null)
        {
            options = new SessionOptions();
            options.AppendExecutionProvider_DML(); // DML 多线程使用 Run 有问题，不使用DML/加锁/new新对象可解决
            options.AppendExecutionProvider_CPU();
        }

        _det = new(detModelPath, options);
        _rec = new(recModelPath, options);
        _dict = File.ReadAllLines(wordDictPath);
    }

    public void Dispose()
    {
        _det.Dispose();
        _rec.Dispose();
    }

    public IReadOnlyList<(Rectangle Box, float Score)> Detect(ReadOnlySpan<byte> image,
                                                              ReadOnlySpan<long> imageShape,
                                                              IReadOnlyList<Rectangle> regions)
    {
        Detector.Preprocess(image, imageShape, regions, out var inputs, out var outputs);
        using var _1 = inputs;
        using var _2 = outputs;
        _det.Run(null, _det.InputNames, inputs, _det.OutputNames, outputs);
        // 用于灰度图测试
        // Detector.Postprocess(outputs, regions, [imageShape[0], imageShape[1], 1], out var gray);
        // using var grayImage = new Bitmap((int)imageShape[1], (int)imageShape[0], System.Drawing.Imaging.PixelFormat.Format8bppIndexed);
        // grayImage.SetGrayPalette();
        // grayImage.CopyFrom(gray);
        // grayImage.Show("gray");
        Detector.Postprocess(outputs, regions, out var boxes);
        return boxes;
    }

    public IReadOnlyList<(string Text, float Score)> Recognize(ReadOnlySpan<byte> image,
                                                               ReadOnlySpan<long> imageShape,
                                                               IReadOnlyList<Rectangle> boxes)
    {
        Recognizer.Preprocess(image, imageShape, boxes, out var inputs, out var outputs);
        using var _1 = inputs;
        using var _2 = outputs;
        _rec.Run(null, _rec.InputNames, inputs, _rec.OutputNames, outputs);
        Recognizer.Postprocess(outputs, _dict, out var texts);
        return texts;
    }

    public IReadOnlyList<((Rectangle Box, float Score) Det, (string Text, float Score) Rec)> DetectAndRecognize(ReadOnlySpan<byte> image,
                                                                                                                ReadOnlySpan<long> imageShape,
                                                                                                                IReadOnlyList<Rectangle> regions)
    {
        var boxes = Detect(image, imageShape, regions);
        if (boxes.Count == 0)
        {
            return [];
        }
        var texts = Recognize(image, imageShape, boxes.Select(x => x.Box).ToArray());
        return boxes.Zip(texts).ToList();
    }

    public IReadOnlyList<(Rectangle Box, float Score)> Detect(Bitmap bitmap, IReadOnlyList<Rectangle> regions)
    {
        return Detect(bitmap.ToBytes(out var imageShape), imageShape, regions);
    }

    public IReadOnlyList<(string Text, float Score)> Recognize(Bitmap bitmap, IReadOnlyList<Rectangle> boxes)
    {
        return Recognize(bitmap.ToBytes(out var imageShape), imageShape, boxes);
    }

    public IReadOnlyList<((Rectangle Box, float Score) Det, (string Text, float Score) Rec)> DetectAndRecognize(Bitmap bitmap, IReadOnlyList<Rectangle> regions)
    {
        return DetectAndRecognize(bitmap.ToBytes(out var imageShape), imageShape, regions);
    }
}
