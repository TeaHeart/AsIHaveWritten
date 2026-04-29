namespace OmniParser;

using Common.Extensions;
using Microsoft.ML.OnnxRuntime;
using System.Drawing;

public sealed class OmniParserEngine : IDisposable
{
    private readonly InferenceSession _det;

    public OmniParserEngine(string detModelPath = "Resources/omni/icon_detect.onnx",
                            SessionOptions? options = null)
    {
        if (options == null)
        {
            options = new SessionOptions();
            options.AppendExecutionProvider_DML();
            options.AppendExecutionProvider_CPU();
        }

        _det = new InferenceSession(detModelPath, options);
    }

    public IReadOnlyList<(Rectangle Box, float Score)> Detect(ReadOnlySpan<byte> image, ReadOnlySpan<long> imageShape)
    {
        using var input = Detector.Preprocess(image, imageShape);
        _det.Run(null, _det.InputNames, [input.Input], _det.OutputNames, [input.Output]);

        return Detector.Postprocess(input.OutputBuffer, input);
    }

    public IReadOnlyList<(Rectangle Box, float Score)> Detect(Bitmap bitmap)
    {
        return Detect(bitmap.ToBytes(out var imageShape), imageShape);
    }

    public void Dispose()
    {
        _det.Dispose();
    }
}