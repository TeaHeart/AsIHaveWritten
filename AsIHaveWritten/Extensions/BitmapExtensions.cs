namespace AsIHaveWritten.Extensions;

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

internal static class BitmapExtensions
{
    internal static Rectangle GetBounds(this Bitmap bitmap)
    {
        return new(0, 0, bitmap.Width, bitmap.Height);
    }

    internal static long[] GetShape(this Bitmap bitmap)
    {
        return [bitmap.Height, bitmap.Width, Math.Max(1, Image.GetPixelFormatSize(bitmap.PixelFormat) / 8)];
    }

    internal static long[] GetStrides(this Bitmap bitmap)
    {
        var shape = bitmap.GetShape();
        var value = shape[1] * shape[2];
        return [(value + 4 - value % 4), shape[2], 1];
    }

    internal static byte[] ToBytes(this Bitmap bitmap, out long[] imageShape, PixelFormat format = PixelFormat.Format24bppRgb)
    {
        var shape = bitmap.GetShape();
        var (height, width, channels) = ((int)shape[0], (int)shape[1], format switch
        {
            PixelFormat.Format8bppIndexed => 1,
            PixelFormat.Format24bppRgb => 3,
            PixelFormat.Format32bppRgb => 3,
            PixelFormat.Format32bppArgb => 3, // 舍弃透明通道
            _ => throw new NotImplementedException($"不支持 {format} 格式")
        });
        var bitmapdata = bitmap.LockBits(bitmap.GetBounds(), ImageLockMode.ReadOnly, format);

        try
        {
            // 处理字节对齐
            var actualStride = width * channels;
            var bytes = new byte[height * actualStride];
            for (int h = 0; h < height; h++)
            {
                Marshal.Copy(bitmapdata.Scan0 + h * bitmapdata.Stride, bytes, h * actualStride, actualStride);
            }
            imageShape = [height, width, channels];
            return bytes;
        }
        finally
        {
            bitmap.UnlockBits(bitmapdata);
        }
    }

    internal static void CopyFrom(this Bitmap bitmap, byte[] bytes)
    {
        var shape = bitmap.GetShape();
        var (height, width, channels) = ((int)shape[0], (int)shape[1], (int)shape[2]);
        var bitmapdata = bitmap.LockBits(bitmap.GetBounds(), ImageLockMode.WriteOnly, bitmap.PixelFormat);

        try
        {
            // 处理字节对齐
            var actualStride = width * channels;
            for (int h = 0; h < height; h++)
            {
                Marshal.Copy(bytes, h * actualStride, bitmapdata.Scan0 + h * bitmapdata.Stride, actualStride);
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapdata);
        }
    }

    internal static void Show(this Bitmap bitmap, string title, bool wait = true, int timeout = Timeout.Infinite)
    {
        var filename = Path.GetTempFileName().Replace(".tmp", $"-{title}.png");
        bitmap.Save(filename);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "rundll32.exe", // 可选 mspaint.exe 
            Arguments = string.Join(" ", ["shimgvw.dll,ImageView_Fullscreen", filename])
        }) ?? throw new NullReferenceException();

        if (timeout != Timeout.Infinite)
        {
            Task.Delay(timeout).ContinueWith(_ => process.Kill());
        }

        if (wait)
        {
            process.WaitForExit();
        }
    }

    internal static void SetGrayPalette(this Bitmap bitmap)
    {
        var palette = bitmap.Palette;
        for (int i = 0; i < 256; i++)
        {
            palette.Entries[i] = Color.FromArgb(i, i, i);
        }
        bitmap.Palette = palette;
    }

    internal static void DrawBoxes(this Bitmap bitmap, IEnumerable<(Rectangle Box, float Score)> boxes)
    {
        using var graph = Graphics.FromImage(bitmap);
        foreach (var (Box, Score) in boxes)
        {
            var pen = Score switch
            {
                >= 0.95f => Pens.Green,
                >= 0.90f => Pens.Blue,
                >= 0.85f => Pens.Gray,
                >= 0.80f => Pens.Yellow,
                >= 0.70f => Pens.Orange,
                >= 0.60f => Pens.Red,
                _ => Pens.Magenta
            };
            graph.DrawRectangle(pen, Box);
        }
    }
}
