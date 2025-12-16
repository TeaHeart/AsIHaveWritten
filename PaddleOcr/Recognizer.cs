namespace PaddleOcr;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System.Text;

public static class Recognizer
{
    public static NamedOnnxValue[] Preprocess(Mat image, Rect[] rois)
    {
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
            using var roiMat = image[roi];

            // 按高48调整宽度
            using var resizedMat = new Mat();
            Cv2.Resize(roiMat, resizedMat, new(roi.Width * 48 / roi.Height, 48));

            unsafe
            {
                resizedMat.ForEachAsVec4b((value, position) =>
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

    public static RecResult[] Postprocess(IReadOnlyCollection<NamedOnnxValue> outputs, string[] index2Word)
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
                if (0 <= maxIndex && maxIndex < index2Word.Length)
                {
                    count++;
                    score += maxScore;
                    sb.Append(index2Word[maxIndex]);
                }
                else if (maxIndex == index2Word.Length)
                {
                    sb.Append(' ');
                }
            }

            score = count > 0 ? score / count : 0;
            result[b] = new(sb.ToString(), score);
        }

        return result;
    }
}
