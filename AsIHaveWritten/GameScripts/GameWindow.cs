namespace AsIHaveWritten.GameScripts;

using AsIHaveWritten.Helpers;
using PaddleOcr;
using SharpHook;
using SharpHook.Data;
using System;
using System.Drawing;
using WindowCapture;

internal class GameWindow : IDisposable
{
    internal CoordinateMapper Mapper => new() { Target = Window.ClientBounds };
    internal readonly WindowMonitor Window;
    internal readonly PaddleOcrEngine Engine;
    internal readonly IEventSimulator Simulator;
    internal readonly IGlobalHook Hook;

    internal GameWindow(string processName)
    {
        Window = new(processName);
        Engine = new();
        Simulator = new EventSimulator();
        Hook = new SimpleGlobalHook();
        Hook.RunAsync();
    }

    public void Dispose()
    {
        Window.Dispose();
        Engine.Dispose();
        //_hook.Dispose(); // 会停止全局的hook 😅
    }

    internal Color? GetPixel(Point point)
    {
        using var image = Window.Screenshot;
        if (image == null)
        {
            return null;
        }
        point = Mapper.NormalizedTarget.Map(point);
        return image.GetPixel(point.X, point.Y);
    }

    internal IReadOnlyList<string> DetectAndRecognize(IReadOnlyList<Rectangle> regions)
    {
        using var image = Window.Screenshot;
        if (image == null)
        {
            return Enumerable.Repeat(string.Empty, regions.Count).ToArray();
        }
        var mapper = Mapper.NormalizedTarget;
        return regions.Select(x => string.Join("", Engine.DetectAndRecognize(image, [mapper.Map(x)]).Select(x => x.Rec.Text))).ToArray();
    }

    internal IReadOnlyList<string> Recognize(IReadOnlyList<Rectangle> regions)
    {
        using var image = Window.Screenshot;
        if (image == null)
        {
            return Enumerable.Repeat(string.Empty, regions.Count).ToArray();
        }
        var mapper = Mapper.NormalizedTarget;
        return Engine.Recognize(image, regions.Select(mapper.Map).ToArray()).Select(x => x.Text).ToArray();
    }

    internal void MouseMove(Point point)
    {
        point = Mapper.Map(point);
        Simulator.SimulateMouseMovement((short)point.X, (short)point.Y);
    }

    internal void MouseClick(MouseButton button = MouseButton.Button1, int ms = 0)
    {
        Simulator.SimulateMousePress(button);
        Thread.Sleep(ms);
        Simulator.SimulateMouseRelease(button);
    }

    internal void KeyClick(KeyCode key, int ms = 0)
    {
        Simulator.SimulateKeyPress(key);
        Thread.Sleep(ms);
        Simulator.SimulateKeyRelease(key);
    }
}
