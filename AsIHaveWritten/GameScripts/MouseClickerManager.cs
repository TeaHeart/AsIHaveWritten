namespace AsIHaveWritten.GameScripts;

using AsIHaveWritten.Extensions;
using AsIHaveWritten.Helpers;
using SharpHook;
using SharpHook.Data;
using System.Drawing;

public class MouseClickerManager : IDisposable
{
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
                _hook.KeyPressed += OnKeyPressed;
                _hook.KeyReleased += OnKeyKeyReleased;
            }
            else
            {
                _hook.KeyPressed -= OnKeyPressed;
                _hook.KeyReleased -= OnKeyKeyReleased;
                _clicker.Enabled = false;
            }
        }
    }

    private readonly IGlobalHook _hook;
    private readonly IEventSimulator _simulator;
    private readonly MouseClicker _clicker;
    private readonly KeyCode _hotkey;
    private Point? _clickPoint;
    private Point? _restorePoint;
    private bool _enabled;

    public MouseClickerManager(IGlobalHook hook, IEventSimulator simulator, KeyCode hotkey = KeyCode.VcF8)
    {
        _hook = hook;
        _simulator = simulator;
        _clicker = new(simulator) { Button = MouseButton.Button1 };
        _hotkey = hotkey;
    }

    public void Dispose()
    {
        _hook.KeyPressed -= OnKeyPressed;
        _hook.KeyReleased -= OnKeyKeyReleased;
        _clicker.Dispose();
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == KeyCode.VcLeftControl)
        {
            if (!_clicker.Enabled)
            {
                if (_clickPoint is Point)
                {
                    _restorePoint = Win32Helper.GetCursorPos();
                }
                _clicker.Enabled = true;
            }
        }
    }

    private void OnKeyKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == KeyCode.VcLeftControl)
        {
            if (_clicker.Enabled)
            {
                if (_restorePoint is Point p)
                {
                    _simulator.SimulateMouseMovement(p);
                    _restorePoint = null;
                }
                _clicker.Enabled = false;
            }
        }

        if (e.Data.KeyCode == _hotkey)
        {
            if (_clickPoint is Point)
            {
                _clickPoint = null;
                Console.WriteLine("已清除点");
            }
            else
            {
                _clickPoint = Win32Helper.GetCursorPos();
                Console.WriteLine($"已记录点 {_clickPoint}");
            }
            _clicker.Location = _clickPoint;
        }
    }
}
