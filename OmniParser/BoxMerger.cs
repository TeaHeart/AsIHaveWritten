namespace OmniParser;

using OmniParser.Models;
using System.Drawing;

internal static class BoxMerger
{
    /// <summary>
    /// Merge YOLO detection boxes with OCR text boxes.
    /// If an OCR box is inside/overlapping an icon box, the OCR text is assigned as the icon's content.
    /// Ported from OmniParser's remove_overlap_new.
    /// </summary>
    public static List<ParsedElement> Merge(
        List<YoloDetector.DetectedBox> yoloBoxes,
        IReadOnlyList<(Rectangle Box, string Text, float Score)> ocrResults,
        Size imageSize)
    {
        var elements = new List<ParsedElement>();

        // 添加 OCR 文本框
        foreach (var ocr in ocrResults)
        {
            elements.Add(new ParsedElement(
                ocr.Box,
                "text",
                ocr.Text,
                Interactivity: false,
                ocr.Score
            ));
        }

        // 排序：无内容的 icon 排在后面（方便后续合并）
        var sortedYolo = yoloBoxes
            .OrderBy(b => BoxContainsAnyOcr(b.Box, ocrResults) ? 1 : 0)
            .ToList();

        foreach (var yolo in sortedYolo)
        {
            var box = yolo.Box;
            var containOcr = elements
                .Where(e => e.Type == "text" && OverlapRatio(e.Box, box) > 0.8)
                .ToList();

            string? content = null;
            if (containOcr.Count > 0)
            {
                // 合并被包含的 OCR 文本
                content = string.Join(" ", containOcr.Select(e => e.Content));
                foreach (var ocr in containOcr)
                {
                    elements.Remove(ocr);
                }
            }

            elements.Add(new ParsedElement(
                box,
                "icon",
                content,
                Interactivity: true,
                yolo.Confidence
            ));
        }

        return elements;
    }

    private static bool BoxContainsAnyOcr(Rectangle box, IReadOnlyList<(Rectangle Box, string Text, float Score)> ocrResults)
    {
        return ocrResults.Any(o => OverlapRatio(o.Box, box) > 0.8);
    }

    /// <summary>
    /// Compute ratio of inner box area that overlaps with outer box.
    /// </summary>
    private static float OverlapRatio(Rectangle inner, Rectangle outer)
    {
        var inter = Rectangle.Intersect(inner, outer);
        if (inter.Width <= 0 || inter.Height <= 0) return 0;

        var innerArea = inner.Width * inner.Height;
        if (innerArea <= 0) return 0;

        var interArea = inter.Width * inter.Height;
        return (float)interArea / innerArea;
    }
}
