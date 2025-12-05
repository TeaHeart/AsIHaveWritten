namespace AsIHaveWritten.Helpers;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Diagnostics;
using System.Drawing;

public class Detector : IDisposable
{
    private readonly InferenceSession _session;

    public Detector(string modelPath)
    {
        _session = new InferenceSession(modelPath);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    public DetResult[] Detect(Bitmap image)
    {
        var s = Stopwatch.StartNew();
        var inputs = PreProcess(image);
        using var outputs = _session.Run(inputs);
        var result = PostProcess(outputs);
        return result;
    }

    private IReadOnlyCollection<NamedOnnxValue> PreProcess(Bitmap image)
    {
        using var src = image.ToMat();

        // 归一
        using var dest = new Mat();
        src.ConvertTo(dest, MatType.CV_32FC4, 1.0 / 255.0);

        // 需为32的倍数
        var blockSize = 32;
        var newHeight = (src.Height + blockSize - 1) / blockSize * blockSize;
        var newWidth = (src.Width + blockSize - 1) / blockSize * blockSize;

        // 一个输入 [-1, 1, -1, -1] 数量，通道，高，宽
        var tensor = new DenseTensor<float>([1, 3, newHeight, newWidth]);
        // 预计算
        var dim = tensor.Dimensions;
        var heightStride = dim[3];
        var channelStride = dim[2] * heightStride;
        var batchStride = dim[1] * channelStride;

        unsafe
        {
            dest.ForEachAsVec4f((value, position) =>
            {
                var h = position[0];
                var w = position[1];
                var index = 0 * batchStride + 0 * channelStride + h * heightStride + w;

                var span = tensor.Buffer.Span;
                span[index + 0 * channelStride] = value->Item0; // B
                span[index + 1 * channelStride] = value->Item1; // G
                span[index + 2 * channelStride] = value->Item2; // R
            });
        }

        return [NamedOnnxValue.CreateFromTensor("x", tensor)];
    }

    private DetResult[] PostProcess(IReadOnlyCollection<DisposableNamedOnnxValue> outputs)
    {
        // 一个输出 [-1, 1, -1, -1] 数量，通道，高，宽
        var tensor = (DenseTensor<float>)outputs.First(x => x.Name == "fetch_name_0").Value;
        // 预计算
        var dim = tensor.Dimensions;
        var heightStride = dim[3];
        var channelStride = dim[2] * heightStride;
        var batchStride = dim[1] * channelStride;

        using var det = new Mat(dim[2], dim[3], MatType.CV_8UC1);

        unsafe
        {
            det.ForEachAsByte((value, position) =>
            {
                var h = position[0];
                var w = position[1];
                var index = 0 * batchStride + 0 * channelStride + h * heightStride + w;

                var span = tensor.Buffer.Span;
                var thresh = span[index + 0 * channelStride];
                *value = (byte)(thresh * 255);
            });
        }

        det.FindContours(out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxTC89KCOS);
        var detRect = new Rect(default, det.Size());

        return contours.Select(x =>
        {
            var box = Cv2.BoundingRect(x);

            // 计算平均分
            using var roi = new Mat(det, box);
            var score = (float)roi.Mean().Val0 / 255f;

            // 扩展矩形
            var expendSize = box.Height / 2;
            box.Inflate(expendSize, expendSize);
            box &= detRect;

            return new DetResult { Box = box, Score = score };
        })
        .Where(x => x.Score > 0.6)
        .OrderBy(x => x.Box.Y)
        .ThenBy(x => x.Box.X)
        .ToArray();
    }

    public readonly struct DetResult
    {
        public Rect Box { get; init; }
        public float Score { get; init; }
    }
}
