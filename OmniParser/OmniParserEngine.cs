namespace OmniParser;

using OmniParser.Models;
using Microsoft.ML.OnnxRuntime;
using PaddleOcr;
using System.Drawing;

public sealed class OmniParserEngine : IDisposable
{
    private readonly InferenceSession _yoloSession;
    private readonly PaddleOcrEngine _ocrEngine;
    private readonly string[] _yoloInputNames;
    private readonly string[] _yoloOutputNames;

    /// <summary>
    /// The parsed screen result containing the annotated image and structured elements.
    /// </summary>
    public record ParsedScreen(
        Bitmap AnnotatedImage,
        IReadOnlyList<ParsedElement> Elements
    );

    public OmniParserEngine(
        string yoloModelPath = "Resources/icon_detect.onnx",
        string detModelPath = "Resources/ppocr/PP-OCRv5_mobile_det_infer.onnx",
        string recModelPath = "Resources/ppocr/PP-OCRv5_mobile_rec_infer.onnx",
        string wordDictPath = "Resources/ppocr/characterDict.txt",
        SessionOptions? options = null)
    {
        options ??= CreateDefaultSessionOptions();

        _yoloSession = new InferenceSession(yoloModelPath, options);
        _yoloInputNames = [_yoloSession.InputNames[0]];
        _yoloOutputNames = [_yoloSession.OutputNames[0]];
        _ocrEngine = new PaddleOcrEngine(detModelPath, recModelPath, wordDictPath, options);
    }

    public ParsedScreen ParseScreen(Bitmap screenshot)
    {
        // Step 1: YOLO 检测（预分配输入输出，跟 PaddleOCR 同样的 Run 模式）
        YoloDetector.Preprocess(screenshot, out var resizedImage,
                                out var yoloInput, out var yoloOutput, out var outputBuffer);
        List<YoloDetector.DetectedBox> yoloBoxes;
        try
        {
            var inputs = new List<OrtValue> { yoloInput };
            var outputs = new List<OrtValue> { yoloOutput };
            _yoloSession.Run(null, _yoloInputNames, inputs, _yoloOutputNames, outputs);
            yoloBoxes = YoloDetector.Postprocess(outputBuffer.AsSpan(), resizedImage, screenshot.Size);
        }
        finally
        {
            yoloInput.Dispose();
            yoloOutput.Dispose();
            resizedImage.Dispose();
        }

        // Step 2: OCR 文本检测识别
        var ocrResults = _ocrEngine.DetectAndRecognize(
            screenshot, [new Rectangle(0, 0, screenshot.Width, screenshot.Height)]);

        var ocrList = ocrResults
            .Select(r => (r.Det.Box, r.Rec.Text, r.Rec.Score))
            .ToList();

        // Step 3: 合并框
        var elements = BoxMerger.Merge(yoloBoxes, ocrList, screenshot.Size);

        // Step 4: 标注图像
        var annotated = BoxAnnotator.Annotate(screenshot, elements);

        return new ParsedScreen(annotated, elements);
    }

    public void Dispose()
    {
        _yoloSession?.Dispose();
        _ocrEngine?.Dispose();
    }

    private static SessionOptions CreateDefaultSessionOptions()
    {
        var options = new SessionOptions();
        options.AppendExecutionProvider_DML();
        options.AppendExecutionProvider_CPU();
        return options;
    }
}
