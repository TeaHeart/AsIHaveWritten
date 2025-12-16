namespace PaddleOcr;

using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

public class PaddleOcrEngine : IDisposable
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
            options.AppendExecutionProvider_DML(); // 多线程使用 InferenceSession Run 有问题，不使用DML/加锁/new新对象可解决
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

    public DetResult[] Detect(Mat image, Rect[]? rois = null)
    {
        rois ??= [new(0, 0, image.Width, image.Height)];
        var inputs = Detector.Preprocess(image, rois);
        using var outputs = RunWithLock(_det, inputs);
        var result = Detector.Postprocess(outputs, rois);
        return result;
    }

    public RecResult[] Recognize(Mat image, Rect[]? rois = null)
    {
        rois ??= [new(0, 0, image.Width, image.Height)];
        var inputs = Recognizer.Preprocess(image, rois);
        using var outputs = RunWithLock(_rec, inputs);
        var result = Recognizer.Postprocess(outputs, _dict);
        return result;
    }

    public RecResult[] Recognize(Mat image, DetResult[] dets)
    {
        return Recognize(image, dets.Select(x => x.Box).ToArray());
    }

    public OcrResult[] DetectAndRecognize(Mat image)
    {
        var dets = Detect(image);
        if (dets.Length == 0)
        {
            return [];
        }
        var recs = Recognize(image, dets);
        return dets.Zip(recs, (det, rec) => new OcrResult(det, rec)).ToArray();
    }

    public static void DrawBoxes(Mat image, DetResult[] dets)
    {
        foreach (var item in dets)
        {
            var color = item.Score switch
            {
                > 0.8f => Scalar.Green,
                > 0.6f => Scalar.Yellow,
                _ => Scalar.Red,
            };
            image.Rectangle(item.Box, color);
        }
    }

    private static IDisposableReadOnlyCollection<DisposableNamedOnnxValue> RunWithLock(InferenceSession session, NamedOnnxValue[] inputs)
    {
        lock (session)
        {
            return session.Run(inputs);
        }
    }
}
