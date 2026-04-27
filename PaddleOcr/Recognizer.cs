namespace PaddleOcr;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;
using System.Text;

internal static class Recognizer
{
    private static void CreateOrtValue(IReadOnlyList<Rectangle> regions, out OrtValue inputOrt, out OrtValue outputOrt)
    {
        // 所有区域 统一 48 * width
        var newHeight = 48;
        var newWidth = regions.Max(x => x.Width * newHeight / x.Height);
        // 输出长度 (width - 4) / 8 向上取整
        var blocksize = 8;
        var outputLength = (int)Math.Ceiling((newWidth - blocksize / 2f) / blocksize);

        // 一个输入 [-1, 3, 48, -1] 数量 通道 高度 宽度
        // 一个输出 [-1, -1, 18385] 数量 长度 概率
        var inputShape = new long[] { regions.Count, 3, newHeight, newWidth };
        var outputShape = new long[] { regions.Count, outputLength, 18385 };

        var inputArray = new float[ShapeUtils.GetSizeForShape(inputShape)];
        var outputArray = new float[ShapeUtils.GetSizeForShape(outputShape)];

        inputOrt = OrtValue.CreateTensorValueFromMemory(inputArray, inputShape);

        try
        {
            outputOrt = OrtValue.CreateTensorValueFromMemory(outputArray, outputShape);
        }
        catch
        {
            inputOrt.Dispose();
            throw;
        }
    }

    private static void FillInput(ReadOnlySpan<byte> image,
                                  ReadOnlySpan<long> imageShape,
                                  IReadOnlyList<Rectangle> regions,
                                  Span<float> input,
                                  ReadOnlySpan<long> inputShape)
    {
        // 一个输入 [-1, 3, 48, -1] 数量 通道 高度 宽度
        var scale = 1f / 255f;

        var imageStrides = ShapeUtils.GetStrides(imageShape);
        var (rowStride, channels, _) = ((int)imageStrides[0], (int)imageStrides[1], (int)imageStrides[2]);
        var inputStrides = ShapeUtils.GetStrides(inputShape);
        var (batchStride, channelsStride, heightStride, _) = ((int)inputStrides[0], (int)inputStrides[1], (int)inputStrides[2], (int)inputStrides[3]);

        for (int batch = 0; batch < regions.Count; batch++)
        {
            var batchStart = batch * batchStride;
            var region = regions[batch];
            var regionStart = region.Y * rowStride + region.X * channels;

            var srcHeight = region.Height;
            var srcWidth = region.Width;
            // 高固定48宽自适应
            var destHeight = 48;
            var destWidth = srcWidth * destHeight / srcHeight;

            // resize 并填充
            var scaleX = (float)srcWidth / destWidth;
            var scaleY = (float)srcHeight / destHeight;

            for (int y = 0; y < destHeight; y++)
            {
                var heightStart = batchStart + 0 * channelsStride + y * heightStride;
                for (int x = 0; x < destWidth; x++)
                {
                    // 源图像中的对应位置
                    var srcX = (x + 0.5f) * scaleX - 0.5f;
                    var srcY = (y + 0.5f) * scaleY - 0.5f;

                    srcY = Math.Clamp(srcY, 0, srcHeight - 1);
                    srcX = Math.Clamp(srcX, 0, srcWidth - 1);

                    // 四个相邻像素
                    var y1 = (int)Math.Floor(srcY);
                    var x1 = (int)Math.Floor(srcX);
                    var y2 = Math.Min(y1 + 1, srcHeight - 1);
                    var x2 = Math.Min(x1 + 1, srcWidth - 1);

                    // 插值权重
                    var wy = srcY - y1;
                    var wx = srcX - x1;

                    var p11Start = regionStart + y1 * rowStride + x1 * channels;
                    var p12Start = regionStart + y1 * rowStride + x2 * channels;
                    var p21Start = regionStart + y2 * rowStride + x1 * channels;
                    var p22Start = regionStart + y2 * rowStride + x2 * channels;
                    var inputStart = heightStart + x;

                    for (int c = 0; c < 3; c++)
                    {
                        // 获取四个相邻像素的值
                        var p11 = image[p11Start + c];
                        var p12 = image[p12Start + c];
                        var p21 = image[p21Start + c];
                        var p22 = image[p22Start + c];

                        // 双线性插值计算
                        var value = (1 - wx) * (1 - wy) * p11 + wx * (1 - wy) * p12 + +(1 - wx) * wy * p21 + wx * wy * p22;
                        var color = (byte)Math.Clamp(Math.Round(value), 0, 255);

                        input[inputStart + c * channelsStride] = color * scale;
                    }
                }
            }
        }
    }

    internal static void Preprocess(ReadOnlySpan<byte> image,
                                    ReadOnlySpan<long> imageShape,
                                    IReadOnlyList<Rectangle> regions,
                                    out IDisposableReadOnlyCollection<OrtValue> inputs,
                                    out IDisposableReadOnlyCollection<OrtValue> outputs)
    {
        // 一个输入 [-1, 3, 48, -1] 数量 通道 高度 宽度
        // 一个输出 [-1, -1, 18385] 数量 长度 概率
        CreateOrtValue(regions, out var input, out var output);

        try
        {
            FillInput(image,
                       imageShape,
                       regions,
                       input.GetTensorMutableDataAsSpan<float>(),
                       input.GetTensorTypeAndShape().Shape);
            inputs = new DisposableList<OrtValue>([input]);
            outputs = new DisposableList<OrtValue>([output]);
        }
        catch
        {
            input.Dispose();
            output.Dispose();
            throw;
        }
    }

    private static void CtcLabelDecode(ReadOnlySpan<float> output,
                                       ReadOnlySpan<long> outputShape,
                                       ReadOnlySpan<string> index2Word,
                                       out IReadOnlyList<(string Text, float Score)> texts)
    {
        // 一个输出 [-1, -1, 18385] 数量 长度 概率
        var (batchSize, seqLength, classes) = ((int)outputShape[0], (int)outputShape[1], (int)outputShape[2]);
        var outputStrides = ShapeUtils.GetStrides(outputShape);
        var (batchStride, seqStride, _) = ((int)outputStrides[0], (int)outputStrides[1], (int)outputStrides[2]);
        var list = new List<(string, float)>();

        for (int b = 0; b < batchSize; b++)
        {
            var batchStart = b * batchStride;
            var sum = 0f;
            var indices = new List<int>([-1]);

            // 找最大索引
            for (int t = 0; t < seqLength; t++)
            {
                var seqStart = batchStart + seqStride * t;
                var maxIndex = -1;
                var maxScore = float.MinValue;

                for (int c = 0; c < classes; c++)
                {
                    var index = seqStart + c;
                    var value = output[index];
                    if (value > maxScore)
                    {
                        maxScore = value;
                        maxIndex = c;
                    }
                }

                if (indices[^1] != maxIndex)
                {
                    indices.Add(maxIndex);
                    if (maxIndex != 0)
                    {
                        sum += maxScore;
                    }
                }
            }

            // index2word
            var sb = new StringBuilder();
            foreach (var item in indices)
            {
                var index = item - 1;
                if (0 <= index && index < index2Word.Length)
                {
                    sb.Append(index2Word[index]);
                }
            }

            list.Add((sb.ToString(), sb.Length > 0 ? sum / sb.Length : 0));
        }

        texts = list;
    }

    internal static void Postprocess(IDisposableReadOnlyCollection<OrtValue> outputs,
                                     ReadOnlySpan<string> index2Word,
                                     out IReadOnlyList<(string Text, float Score)> texts)
    {
        // 一个输出 [-1, -1, 18385] 数量 长度 概率
        var outputShape = outputs[0].GetTensorTypeAndShape().Shape;
        var output = outputs[0].GetTensorDataAsSpan<float>();

        CtcLabelDecode(output, outputShape, index2Word, out texts);
    }
}
