namespace PaddleOcr;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

public static class Detector
{
    public static NamedOnnxValue[] Preprocess(Mat image, Rect[] rois)
    {
        var maxHeight = rois.Max(x => x.Height);
        var maxWidth = rois.Max(x => x.Width);
        // 需为32的倍数
        var blockSize = 32;
        var newHeight = (maxHeight + blockSize - 1) / blockSize * blockSize;
        var newWidth = (maxWidth + blockSize - 1) / blockSize * blockSize;
        // 一个输入 [-1, 1, -1, -1] 数量，通道，高，宽
        var tensor = new DenseTensor<float>([rois.Length, 3, newHeight, newWidth]);
        // 预计算
        var dim = tensor.Dimensions;
        var heightStride = dim[3];
        var channelStride = dim[2] * heightStride;
        var batchStride = dim[1] * channelStride;

        unsafe
        {
            for (int b = 0; b < rois.Length; b++)
            {
                using var roiMat = image[rois[b]];

                roiMat.ForEachAsVec4b((value, position) =>
                {
                    var h = position[0];
                    var w = position[1];
                    var index = b * batchStride + 0 * channelStride + h * heightStride + w;

                    var span = tensor.Buffer.Span;
                    span[index + 0 * channelStride] = value->Item0 / 255f; // B
                    span[index + 1 * channelStride] = value->Item1 / 255f; // G
                    span[index + 2 * channelStride] = value->Item2 / 255f; // R
                });
            }
        }

        return [NamedOnnxValue.CreateFromTensor("x", tensor)];
    }

    public static DetResult[] Postprocess(IReadOnlyCollection<DisposableNamedOnnxValue> outputs, Rect[] rois)
    {
        // 一个输出 [-1, 1, -1, -1] 数量，通道，高，宽
        var tensor = (DenseTensor<float>)outputs.First(x => x.Name == "fetch_name_0").Value;
        // 预计算
        var dim = tensor.Dimensions;
        var heightStride = dim[3];
        var channelStride = dim[2] * heightStride;
        var batchStride = dim[1] * channelStride;
        var result = new DetResult[dim[0]][];

        for (int b = 0; b < dim[0]; b++)
        {
            using var detImage = new Mat(dim[2], dim[3], MatType.CV_8UC1);

            unsafe
            {
                detImage.ForEachAsByte((value, position) =>
                {
                    var h = position[0];
                    var w = position[1];
                    var index = b * batchStride + 0 * channelStride + h * heightStride + w;

                    var span = tensor.Buffer.Span;
                    *value = (byte)(span[index + 0 * channelStride] * 255);
                });
            }

            detImage.FindContours(out var contours, out var _, RetrievalModes.External, ContourApproximationModes.ApproxNone);
            var roi = rois[b];
            var basePoint = roi.Location;
            var boundary = roi - basePoint;

            result[b] = contours.Select(x =>
            {
                // 最小外接矩形
                var box = Cv2.BoundingRect(x);

                // 计算平均分
                using var boxMat = detImage[box];
                var score = (float)boxMat.Mean().Val0 / 255;

                // 扩展矩形
                var expendSize = box.Height / 2;
                box.Inflate(expendSize, expendSize);
                box &= boundary;

                // 还原x,y坐标
                box += basePoint;
                return new DetResult { Box = box, Score = score };
            }).Where(x => x.Score > 0.6f)
              .ToArray();
        }

        return result.SelectMany(x => x)
                     .OrderBy(x => x.Box.Y)
                     .ThenBy(x => x.Box.X)
                     .ToArray();
    }
}
