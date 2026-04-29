namespace McpServer;

using System.Drawing;

public readonly record struct ScreenElement(
    string Type,
    Rectangle Box,
    string? Content,
    float Score
);

internal static class ScreenElementMerger
{
    public static List<ScreenElement> Merge(
        IReadOnlyList<OmniResult> iconResults,
        IReadOnlyList<OcrResult> ocrResults)
    {
        var elements = new List<ScreenElement>();

        foreach (var item in ocrResults)
        {
            elements.Add(new("text", item.Box, item.Text, item.Score));
        }

        foreach (var item in iconResults.OrderBy(element => BoxContainsAnyOcr(element.Box, ocrResults) ? 1 : 0))
        {
            var box = item.Box;
            var containedOcr = elements
                .Where(e => e.Type == "text" && OverlapRatio(e.Box, box) > 0.8)
                .ToList();

            string? content = null;
            if (containedOcr.Count > 0)
            {
                content = string.Join(" ", containedOcr.Select(e => e.Content));
                foreach (var ocr in containedOcr)
                {
                    elements.Remove(ocr);
                }
            }

            elements.Add(new("icon", box, content, item.Score));
        }

        return elements;
    }

    private static bool BoxContainsAnyOcr(Rectangle box, IReadOnlyList<OcrResult> ocrResults)
    {
        return ocrResults.Any(o => OverlapRatio(o.Box, box) > 0.8);
    }

    private static float OverlapRatio(Rectangle inner, Rectangle outer)
    {
        var inter = Rectangle.Intersect(inner, outer);
        if (inter.Width <= 0 || inter.Height <= 0)
        {
            return 0;
        }

        var innerArea = inner.Width * inner.Height;
        if (innerArea <= 0)
        {
            return 0;
        }

        var interArea = inter.Width * inter.Height;
        return (float)interArea / innerArea;
    }
}