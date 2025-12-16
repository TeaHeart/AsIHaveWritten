namespace AsIHaveWritten.GameScripts;

using AsIHaveWritten.Extensions;
using System.Drawing;

public readonly record struct WindowStatus(IntPtr Handle, Rectangle? Rectangle, bool IsForeground);

public class WindowMonitor : IDisposable
{
    public event EventHandler<WindowStatus>? WindowStatusChanged;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }
            _enabled = value;
            if (value)
            {
                _timer.Enable(100);
            }
            else
            {
                _timer.Disable();
            }
        }
    }

    private readonly GameWindow _window;
    private readonly Timer _timer;
    private WindowStatus _status;
    private bool _enabled;

    public WindowMonitor(GameWindow window)
    {
        _window = window;
        _timer = new(MonitorWindow);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private void MonitorWindow(object? state)
    {
        var newStatus = new WindowStatus(_window.Handle, _window.Rectangle, _window.IsForeground);

        if (_status != newStatus)
        {
            _status = newStatus;
            WindowStatusChanged?.Invoke(this, newStatus);
        }
    }
}
