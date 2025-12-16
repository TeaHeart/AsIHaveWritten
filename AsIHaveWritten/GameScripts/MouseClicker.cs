namespace AsIHaveWritten.GameScripts;

using AsIHaveWritten.Extensions;
using SharpHook;
using SharpHook.Data;
using System.Drawing;

public class MouseClicker : IDisposable
{
    public Point? Location { get; set; }
    public MouseButton Button { get; set; }
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
                Console.WriteLine("开始连点");
            }
            else
            {
                _timer.Disable();
                Console.WriteLine("停止连点");
            }
        }
    }

    private readonly IEventSimulator _simulator;
    private readonly Timer _timer;
    private bool _enabled;

    public MouseClicker(IEventSimulator simulator)
    {
        _simulator = simulator;
        _timer = new(MouseClick);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private void MouseClick(object? state)
    {
        _simulator.SimulateMouseClick(Button, Location);
    }
}
