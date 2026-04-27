namespace AsIHaveWritten.Helpers;

using System.Drawing;

internal readonly struct CoordinateMapper
{
    internal Rectangle Source { get; init; } = new(0, 0, 1920, 1080);
    internal Rectangle Target { get; init; }
    internal CoordinateMapper NormalizedSource => new() { Source = new(Point.Empty, Source.Size), Target = Target };
    internal CoordinateMapper NormalizedTarget => new() { Source = Source, Target = new(Point.Empty, Target.Size) };
    internal CoordinateMapper Normalized => new() { Source = new(Point.Empty, Source.Size), Target = new(Point.Empty, Target.Size) };
    internal CoordinateMapper Reversed => new() { Source = Target, Target = Source };
    public CoordinateMapper() { }

    internal Point Map(Point src)
    {
        var fx = (src.X - Source.X) / (float)Source.Width;
        var fy = (src.Y - Source.Y) / (float)Source.Height;

        var x = (int)(fx * Target.Width) + Target.X;
        var y = (int)(fy * Target.Height) + Target.Y;

        return new(x, y);
    }

    internal Size Map(Size src)
    {
        var fw = src.Width / (float)Source.Width;
        var fh = src.Height / (float)Source.Height;

        var w = (int)(fw * Target.Width);
        var h = (int)(fh * Target.Height);

        return new(w, h);
    }

    internal Rectangle Map(Rectangle src)
    {
        return new(Map(src.Location), Map(src.Size));
    }
}
