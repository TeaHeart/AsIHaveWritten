namespace AsIHaveWritten.GameScripts;

using AsIHaveWritten.Extensions;
using SharpHook.Data;
using System.Drawing;

internal class MouseClicker : IDisposable
{
    internal Point? Location { get; set; }
    internal MouseButton Button { get; set; }
    internal bool Enabled
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
                Console.WriteLine("开始连点");
            }
            else
            {
                _timer.Disable();
                Console.WriteLine("停止连点");
            }
        }
    }

    private readonly GameWindow _window;
    private readonly Timer _timer;
    private bool _enabled;

    internal MouseClicker(GameWindow window)
    {
        _window = window;
        _timer = new(MouseClick);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private void MouseClick(object? state)
    {
        if (Location is Point p)
        {
            _window.MouseMove(p);
        }

        _window.MouseClick(Button);
    }
}
