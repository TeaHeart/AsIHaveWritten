namespace AsIHaveWritten.GameScripts;

using AsIHaveWritten.Helpers;
using SharpHook;
using SharpHook.Data;
using System.Drawing;

internal class MouseClickerManager : IDisposable
{
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
                _window.Hook.KeyPressed += OnKeyPressed;
                _window.Hook.KeyReleased += OnKeyKeyReleased;
                Console.WriteLine("启动鼠标点击器");
            }
            else
            {
                _window.Hook.KeyPressed -= OnKeyPressed;
                _window.Hook.KeyReleased -= OnKeyKeyReleased;
                _clicker.Enabled = false;
                Console.WriteLine("禁用鼠标点击器");
            }
        }
    }

    private readonly GameWindow _window;
    private readonly MouseClicker _clicker;
    private readonly KeyCode _hotkey;
    private Point? _clickPoint;
    private Point? _restorePoint;
    private bool _enabled;

    internal MouseClickerManager(GameWindow window, KeyCode hotkey = KeyCode.VcF8)
    {
        _window = window;
        _clicker = new(window) { Button = MouseButton.Button1 };
        _hotkey = hotkey;
    }

    public void Dispose()
    {
        _window.Hook.KeyPressed -= OnKeyPressed;
        _window.Hook.KeyReleased -= OnKeyKeyReleased;
        _clicker.Dispose();
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == KeyCode.VcLeftControl)
        {
            if (!_clicker.Enabled)
            {
                if (_clickPoint is Point && WindowHelper.GetCursorPos(out var point))
                {
                    _restorePoint = point;
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
                    _window.MouseMove(p);
                    _restorePoint = null;
                }
                _clicker.Enabled = false;
            }
        }

        if (e.Data.KeyCode == _hotkey)
        {
            if (_clickPoint is not Point && WindowHelper.GetCursorPos(out var point))
            {
                _clickPoint = point;
                Console.WriteLine($"已记录点 {_clickPoint}");
            }
            else
            {
                _clickPoint = null;
                Console.WriteLine("已清除点");
            }
            _clicker.Location = _clickPoint;
        }
    }
}
