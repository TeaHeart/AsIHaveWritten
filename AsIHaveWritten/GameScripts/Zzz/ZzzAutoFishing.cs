namespace AsIHaveWritten.GameScripts.Zzz;

using AsIHaveWritten.Extensions;
using AsIHaveWritten.Helpers;
using OpenCvSharp.Extensions;
using PaddleOcr;
using SharpHook;
using SharpHook.Data;
using System.Drawing;

public class ZzzAutoFishing : IDisposable
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
                _timer.Enable(500);
                Console.WriteLine("启用钓鱼");
            }
            else
            {
                _timer.Disable();
                Console.WriteLine("禁用钓鱼");
            }
        }
    }

    private readonly IEventSimulator _simulator;
    private readonly PaddleOcrEngine _engine;
    private readonly GameWindow _window;
    private readonly Timer _timer;
    private readonly Rectangle _tipsBox;
    private readonly Rectangle _fishingArea;
    private readonly Point _rodBtn;
    private readonly Point _closefishRanBtn;
    private readonly Point _closefishDescBtn;
    private bool _enabled;

    public ZzzAutoFishing(IEventSimulator simulator, PaddleOcrEngine engine, GameWindow window)
    {
        _simulator = simulator;
        _engine = engine;
        _window = window;
        _timer = new(Fishing);
        _tipsBox = new(700, 175, 500, 48);
        _fishingArea = new(340, 110, 1200, 700);
        _rodBtn = new(1600, 915);
        _closefishRanBtn = new(977, 627);
        _closefishDescBtn = new(968, 1022);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private void Fishing(object? state)
    {
        if (!_window.IsForeground)
        {
            Console.WriteLine("非游戏窗口");
            return;
        }

        if (_window.Bitmap is not Bitmap bitmap)
        {
            Console.WriteLine("截图失败");
            return;
        }

        using var image = bitmap;
        using var mat = image.ToMat();

        if (_window.Mapper is not CoordinateMapper absMapper)
        {
            Console.WriteLine("转换器获取失败");
            return;
        }

        var relMapper = absMapper.NormalizedTarget;
        var tpisRecs = _engine.Recognize(mat, [relMapper.Map(_tipsBox).ToCv()]);
        var title = tpisRecs[0].Text;
        Console.WriteLine(title);

        if (title.Contains("抛竿"))
        {
            TapKey(KeyCode.VcF);
        }
        else if (title.Contains("等待"))
        {
            // pass
        }
        else if (title.Contains("时机"))
        {
            var btn = relMapper.Map(_rodBtn);
            if (image.GetPixel(btn.X, btn.Y).R >= 192) // 偏紫色
            {
                TapKey(KeyCode.VcF);
            }
        }
        else
        {
            var fishingDets = _engine.Detect(mat, [relMapper.Map(_fishingArea).ToCv()]);

            if (fishingDets.Length == 0)
            {
                return;
            }

            var fishingRecs = _engine.Recognize(mat, fishingDets);
            var text = string.Join("", fishingRecs.Select(x => x.Text));
            Console.WriteLine(text);

            if (text.Contains("鱼跑了"))
            {
                LeftClick(absMapper.Map(_closefishRanBtn));
            }
            if (text.Contains("KG"))
            {
                LeftClick(absMapper.Map(_closefishDescBtn));
            }
            else if (text.Contains("长按"))
            {
                PullRod(3000);
            }
            else if (text.Contains("连点"))
            {
                for (int i = 0; i < 20; i++) // 2s
                {
                    PullRod(90);
                    Thread.Sleep(10);
                }
            }
        }
    }

    private void TapKey(KeyCode code)
    {
        _simulator.SimulateKeyClick([code], 10);
    }

    private void LeftClick(Point location)
    {
        _simulator.SimulateMouseClick(MouseButton.Button1, location);
    }

    private void PullRod(int ms)
    {
        _simulator.SimulateKeyClick([KeyCode.VcA, KeyCode.VcD, KeyCode.VcSpace], ms);
    }
}
