namespace PaddleOcr;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;

internal static class Detector
{
    private static void CreateOrtValue(IReadOnlyList<Rectangle> regions, out OrtValue inputOrt, out OrtValue outputOrt)
    {
        // 统一所有区域宽高
        var maxHeight = regions.Max(x => x.Height);
        var maxWidth = regions.Max(x => x.Width);
        // 调整为32的倍数
        var blockSize = 32;
        var newHeight = maxHeight + blockSize - maxHeight % blockSize;
        var newWidth = maxWidth + blockSize - maxWidth % blockSize;

        // 一个输入 [-1, 1, -1, -1] 数量 通道 高 宽
        // 一个输出 [-1, 1, -1, -1] 数量 通道 高 宽
        var inputShape = new long[] { regions.Count, 3, newHeight, newWidth };
        var outputShape = new long[] { regions.Count, 1, newHeight, newWidth };

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
        // 一个输入 [-1, 1, -1, -1] 数量 通道 高 宽
        var scale = 1f / 255f;
        var mean = new[] { 0.485f, 0.456f, 0.406f };
        var std = new[] { 0.229f, 0.224f, 0.225f };

        var imageStrides = ShapeUtils.GetStrides(imageShape);
        var (rowStride, channels, _) = ((int)imageStrides[0], (int)imageStrides[1], (int)imageStrides[2]);
        var inputStrides = ShapeUtils.GetStrides(inputShape);
        var (batchStride, channelsStride, heightStride, _) = ((int)inputStrides[0], (int)inputStrides[1], (int)inputStrides[2], (int)inputStrides[3]);

        for (int batch = 0; batch < regions.Count; batch++)
        {
            var batchStart = batch * batchStride;
            var region = regions[batch];
            var regionStart = region.Y * rowStride + region.X * channels;
            for (int row = 0; row < region.Height; row++)
            {
                var rowStart = regionStart + row * rowStride;
                var heightStart = batchStart + 0 * channelsStride + row * heightStride;
                for (int column = 0; column < region.Width; column++)
                {
                    var imageStart = rowStart + column * channels;
                    var inputStart = heightStart + column;
                    // BGR 顺序, 但 paddle 源码默认用 RGB 的 mean 和 std 😅
                    input[inputStart + 0 * channelsStride] = (image[imageStart + 0] * scale - mean[0]) / std[0];
                    input[inputStart + 1 * channelsStride] = (image[imageStart + 1] * scale - mean[1]) / std[1];
                    input[inputStart + 2 * channelsStride] = (image[imageStart + 2] * scale - mean[2]) / std[2];
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
        // 一个输入 [-1, 1, -1, -1] 数量 通道 高 宽
        // 一个输出 [-1, 1, -1, -1] 数量 通道 高 宽
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

    private static void FindBounds(ReadOnlySpan<float> span,
                                   int row,
                                   int column,
                                   int height,
                                   int width,
                                   int heightStride,
                                   float thresh,
                                   Span<bool> visited,
                                   ref float sum,
                                   ref int count,
                                   ref Rectangle bounds)
    {
        if (!(0 <= row && row < height && 0 <= column && column < width))
        {
            return;
        }

        var index = row * heightStride + column;
        if (visited[index])
        {
            return;
        }

        visited[index] = true;
        var value = span[index];
        if (!(value >= thresh))
        {
            return;
        }

        sum += value;
        count++;

        // 更新最小外接矩形
        bounds = Rectangle.Union(bounds, new(column, row, 0, 0));

        // 上 右 下 左
        FindBounds(span, row - 1, column, height, width, heightStride, thresh, visited, ref sum, ref count, ref bounds);
        FindBounds(span, row, column + 1, height, width, heightStride, thresh, visited, ref sum, ref count, ref bounds);
        FindBounds(span, row + 1, column, height, width, heightStride, thresh, visited, ref sum, ref count, ref bounds);
        FindBounds(span, row, column - 1, height, width, heightStride, thresh, visited, ref sum, ref count, ref bounds);
    }

    private static void FindAllBounds(ReadOnlySpan<float> output,
                                      ReadOnlySpan<long> outputShape,
                                      IReadOnlyList<Rectangle> regions,
                                      out IReadOnlyList<(Rectangle Box, float Score)> boxes)
    {
        // 一个输出 [-1, 1, -1, -1] 数量 通道 高 宽
        var thresh = 0.3f;
        var boxThresh = 0.6f;
        var unclipRatio = 1.5f - 0.5f;
        var maxCandidates = 1000;

        var outputStrides = ShapeUtils.GetStrides(outputShape);
        var (batchStride, _, heightStride, _) = ((int)outputStrides[0], (int)outputStrides[1], (int)outputStrides[2], (int)outputStrides[3]);
        var list = new List<(Rectangle Box, float Score)>(maxCandidates);

        for (int batch = 0; batch < regions.Count; batch++)
        {
            var batchStart = batch * batchStride;
            var region = regions[batch];
            var (x, y, width, height) = (region.X, region.Y, region.Width, region.Height);
            var visited = new bool[height * heightStride];

            for (int row = 0; row < height; row++)
            {
                var rowStart = row * heightStride;
                for (int column = 0; column < width; column++)
                {
                    var index = rowStart + column;
                    if (!visited[index] && output[index] >= thresh)
                    {
                        var sub = output.Slice(batchStart, batchStride);
                        var sum = 0f;
                        var count = 0;
                        var bounds = new Rectangle(column, row, 0, 0);
                        FindBounds(sub, row, column, height, width, heightStride, thresh, visited, ref sum, ref count, ref bounds);

                        var score = count != 0 ? sum / count : 0;
                        if (score >= boxThresh && bounds.Height * bounds.Width > 0)
                        {
                            var expand = (int)(bounds.Height * unclipRatio);
                            bounds.Inflate(expand, expand); // 扩大
                            bounds.Offset(x, y); // 还原坐标
                            bounds.Intersect(region); // 与边界交集
                            list.Add((bounds, score));
                        }
                    }
                }
            }
        }

        boxes = list; // TODO 取前1000, 排序 测试全都要
    }

    private static void CopyToGray(ReadOnlySpan<float> output,
                                   ReadOnlySpan<long> outputShape,
                                   IReadOnlyList<Rectangle> regions,
                                   ReadOnlySpan<long> imageShape,
                                   out byte[] image)
    {
        // 一个输出 [-1, 1, -1, -1] 数量 通道 高 宽
        var outputStrides = ShapeUtils.GetStrides(outputShape);
        var (batchStride, channelsStride, heightStride, _) = ((int)outputStrides[0], (int)outputStrides[1], (int)outputStrides[2], (int)outputStrides[3]);
        var imageStrides = ShapeUtils.GetStrides(imageShape);
        var (rowStride, channels, _) = ((int)imageStrides[0], (int)imageStrides[1], (int)imageStrides[2]);
        image = new byte[ShapeUtils.GetSizeForShape(imageShape)];

        for (int batch = 0; batch < regions.Count; batch++)
        {
            var batchStart = batch * batchStride;
            var region = regions[batch];
            var regionStart = region.Y * rowStride + region.X * channels;
            for (int row = 0; row < region.Height; row++)
            {
                var heightStart = batchStart + 0 * channelsStride + row * heightStride;
                var rowStart = regionStart + row * rowStride;
                for (int column = 0; column < region.Width; column++)
                {
                    var outputStart = heightStart + column;
                    var imageStart = rowStart + column * channels;
                    image[imageStart] = (byte)(output[outputStart] * byte.MaxValue);
                }
            }
        }
    }

    internal static void Postprocess(IDisposableReadOnlyCollection<OrtValue> outputs,
                                     IReadOnlyList<Rectangle> regions,
                                     out IReadOnlyList<(Rectangle Box, float Score)> boxes)
    {
        // 一个输出 [-1, 1, -1, -1] 数量 通道 高 宽
        var outputShape = outputs[0].GetTensorTypeAndShape().Shape;
        var output = outputs[0].GetTensorDataAsSpan<float>();

        FindAllBounds(output, outputShape, regions, out boxes);
    }

    internal static void Postprocess(IDisposableReadOnlyCollection<OrtValue> outputs,
                                     IReadOnlyList<Rectangle> regions,
                                     ReadOnlySpan<long> imageShape,
                                     out byte[] image)
    {
        // 一个输出 [-1, 1, -1, -1] 数量 通道 高 宽
        var outputShape = outputs[0].GetTensorTypeAndShape().Shape;
        var output = outputs[0].GetTensorDataAsSpan<float>();

        CopyToGray(output, outputShape, regions, imageShape, out image);
    }
}
