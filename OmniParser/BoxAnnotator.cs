namespace OmniParser;

using System.Drawing;
using System.Drawing.Imaging;

internal static class BoxAnnotator
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb(0xFF, 0x00, 0x00), // 红
        Color.FromArgb(0x00, 0xFF, 0x00), // 绿
        Color.FromArgb(0x00, 0x00, 0xFF), // 蓝
        Color.FromArgb(0xFF, 0xA5, 0x00), // 橙
        Color.FromArgb(0x80, 0x00, 0x80), // 紫
    ];

    /// <summary>
    /// Draw numbered bounding boxes on the image with labels.
    /// Returns a new bitmap with annotations.
    /// </summary>
    public static Bitmap Annotate(
        Bitmap source,
        IReadOnlyList<Models.ParsedElement> elements,
        float textScale = 0.5f,
        int thickness = 3,
        int textPadding = 10)
    {
        var annotated = new Bitmap(source);
        using var g = Graphics.FromImage(annotated);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        using var font = new Font("Consolas", 10 * textScale, FontStyle.Bold);

        for (int i = 0; i < elements.Count; i++)
        {
            var elem = elements[i];
            var box = elem.Box;
            var color = Palette[i % Palette.Length];

            // 绘制矩形框
            using var pen = new Pen(color, thickness);
            g.DrawRectangle(pen, box);

            // 标注编号标签
            var label = $"{i}";
            var labelSize = g.MeasureString(label, font);
            var labelRect = new Rectangle(
                box.X,
                Math.Max(0, box.Y - (int)labelSize.Height - textPadding),
                (int)labelSize.Width + textPadding * 2,
                (int)labelSize.Height + textPadding);

            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, labelRect);

            // 自适应文本颜色（基于亮度）
            var luminance = 0.299f * color.R + 0.587f * color.G + 0.114f * color.B;
            using var textBrush = new SolidBrush(luminance > 160 ? Color.Black : Color.White);
            g.DrawString(label, font, textBrush,
                labelRect.X + textPadding,
                labelRect.Y + textPadding / 2);
        }

        return annotated;
    }
}
