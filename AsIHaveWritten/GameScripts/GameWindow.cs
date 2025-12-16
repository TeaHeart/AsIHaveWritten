namespace AsIHaveWritten.GameScripts;

using AsIHaveWritten.Helpers;
using System.Drawing;

public class GameWindow : IDisposable
{
    public IntPtr Handle => Win32Helper.GetWindowHandle(_processName);
    public Rectangle? Rectangle => Win32Helper.GetClientRectangle(Handle);
    public CoordinateMapper? Mapper => Rectangle != null ? new() { Target = Rectangle.Value } : null;
    public bool IsForeground => Win32Helper.IsForegroundWindow(Handle);
    public Bitmap? Bitmap => _cachedBitmap.Value;

    private readonly string _processName;
    private readonly SimpleCache<Bitmap?> _cachedBitmap;

    public GameWindow(string processName)
    {
        _processName = processName;
        _cachedBitmap = new(
            () => Win32Helper.PrintWindow(Handle),
            TimeSpan.FromMilliseconds(16), // 1000/16=62.5fps
            x => x?.Clone() as Bitmap,
            x => x?.Dispose());
    }

    public void Dispose()
    {
        _cachedBitmap.Dispose();
    }
}
