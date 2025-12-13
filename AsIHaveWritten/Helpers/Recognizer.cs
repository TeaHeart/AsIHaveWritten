namespace AsIHaveWritten.Helpers;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Drawing;
using System.Text;

public class Recognizer : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string[] _characterDict;

    public Recognizer(string modelPath, string characterDictPath)
    {
        _characterDict = File.ReadAllLines(characterDictPath);
        var options = new SessionOptions();
        options.AppendExecutionProvider_DML();
        options.AppendExecutionProvider_CPU();
        _session = new InferenceSession(modelPath, options);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    public RecResult[] Recognize(Mat image, Rect[] rois)
    {
        var inputs = PreProcess(image, rois);
        using var outputs = _session.Run(inputs);
        var result = PostProcess(outputs, rois);
        return result;
    }

    private IReadOnlyCollection<NamedOnnxValue> PreProcess(Mat src, Rect[] rois)
    {
        // 归一
        using var dest = new Mat();
        src.ConvertTo(dest, MatType.CV_32FC4, 1.0 / 255.0);

        // 一个输入 [-1, 3, 48, -1] 数量，通道，高度，宽度
        var tensor = new DenseTensor<float>([rois.Length, 3, 48, (rois.Max(x => x.Width * 48 / x.Height))]);
        // 预计算
        var dim = tensor.Dimensions;
        var heightStride = dim[3];
        var channelStride = dim[2] * heightStride;
        var batchStride = dim[1] * channelStride;

        for (int b = 0; b < rois.Length; b++)
        {
            var roi = rois[b];
            using var roiMat = new Mat(dest, roi);

            // resize
            using var resizedMat = new Mat();
            Cv2.Resize(roiMat, resizedMat, new(roi.Width * 48 / roi.Height, 48));

            unsafe
            {
                resizedMat.ForEachAsVec4f((value, position) =>
                {
                    var h = position[0];
                    var w = position[1];
                    var index = b * batchStride + 0 * channelStride + h * heightStride + w;

                    var span = tensor.Buffer.Span;
                    span[index + 0 * channelStride] = value->Item0; // B
                    span[index + 1 * channelStride] = value->Item1; // G
                    span[index + 2 * channelStride] = value->Item2; // R
                });
            }
        }

        return [NamedOnnxValue.CreateFromTensor("x", tensor)];
    }

    private RecResult[] PostProcess(IReadOnlyCollection<NamedOnnxValue> outputs, Rect[] rois)
    {
        // 一个输出 [-1, -1, -1] 第几个区域，第几个字符，字符概率
        var tensor = (DenseTensor<float>)outputs.First(x => x.Name == "fetch_name_0").Value;
        // 预计算
        var dim = tensor.Dimensions;
        var typeStride = dim[2];
        var batchStride = dim[1] * typeStride;

        var span = tensor.Buffer.Span;

        var result = new RecResult[dim[0]];

        // 第b个图片
        for (int b = 0; b < dim[0]; b++)
        {
            var batchOffset = b * batchStride;

            var sb = new StringBuilder();
            var count = 0;
            var score = 0f;

            // 第t个字符
            for (int t = 0; t < dim[1]; t++)
            {
                var typeOffset = batchOffset + t * typeStride;

                var maxIndex = -1;
                var maxScore = float.MinValue;

                // 是哪个字符
                for (int i = 0; i < dim[2]; i++)
                {
                    var currValue = span[typeOffset + i];
                    if (!float.IsNaN(currValue) && currValue > maxScore)
                    {
                        maxScore = currValue;
                        maxIndex = i;
                    }
                }

                // 索引有1的偏移
                maxIndex--;
                if (0 <= maxIndex && maxIndex < _characterDict.Length)
                {
                    count++;
                    score += maxScore;
                    sb.Append(_characterDict[maxIndex]);
                }
                else if (maxIndex == _characterDict.Length)
                {
                    sb.Append(' ');
                }
            }

            if (count > 0)
            {
                score /= count;
                result[b] = new RecResult { Box = rois[b], Text = sb.ToString(), Score = score };
            }
        }

        return result;
    }

    public readonly struct RecResult
    {
        public Rect Box { get; init; }
        public float Score { get; init; }
        public string Text { get; init; }
    }
}
