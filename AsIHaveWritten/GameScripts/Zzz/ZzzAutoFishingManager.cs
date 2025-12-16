namespace AsIHaveWritten.GameScripts.Zzz;

using PaddleOcr;
using SharpHook;
using SharpHook.Data;

public class ZzzAutoFishingManager : IDisposable
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
                _hook.KeyReleased += OnKeyKeyReleased;
            }
            else
            {
                _hook.KeyReleased -= OnKeyKeyReleased;
                _autoFishing.Enabled = false;
            }
        }
    }

    private readonly IGlobalHook _hook;
    private readonly ZzzAutoFishing _autoFishing;
    private readonly KeyCode _hotKey;
    private bool _enabled;

    public ZzzAutoFishingManager(IGlobalHook hook, IEventSimulator simulator, PaddleOcrEngine ocrEngine, GameWindow window, KeyCode hotKey = KeyCode.VcF9)
    {
        _hook = hook;
        _autoFishing = new(simulator, ocrEngine, window);
        _hotKey = hotKey;
    }

    public void Dispose()
    {
        _hook.KeyReleased -= OnKeyKeyReleased;
        _autoFishing.Dispose();
    }

    private void OnKeyKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == _hotKey)
        {
            _autoFishing.Enabled = !_autoFishing.Enabled;
        }
    }
}
