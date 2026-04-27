namespace WindowCapture;

using System.Drawing;
using WindowCapture.Helpers;

public sealed class WindowMonitor(string processName, TimeSpan? cacheTime = null) : IDisposable
{
    public nint Handle => Getter(() => _handle);
    public Rectangle ClientBounds => Getter(() => _clientBounds);
    public bool IsForeground => Getter(() => _isForeground);
    public Bitmap? Screenshot => Getter(() => _screenshot?.Clone() as Bitmap);

    private readonly object _syncRoot = new();
    private readonly string _processName = processName;
    private readonly TimeSpan _cacheTime = cacheTime ?? TimeSpan.FromMilliseconds(1000 / 60);
    private bool _disposed;
    private DateTime _lastTime;
    private nint _handle;
    private Rectangle _clientBounds;
    private bool _isForeground;
    private Bitmap? _screenshot;

    private T Getter<T>(Func<T> getter)
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (DateTime.Now - _lastTime > _cacheTime)
            {
                Refresh();
            }
            return getter();
        }
    }

    public void Refresh()
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _handle = WindowHelper.GetWindowHandle(_processName);
            WindowHelper.GetClientBounds(_handle, out _clientBounds);
            _isForeground = WindowHelper.IsForegroundWindow(_handle);
            _screenshot?.Dispose();
            WindowHelper.PrintWindow(_handle, _clientBounds.Size, out _screenshot);
            _lastTime = DateTime.Now;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (!_disposed)
            {
                _screenshot?.Dispose();
                _disposed = true;
            }
        }
    }
}
