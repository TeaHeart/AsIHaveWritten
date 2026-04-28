namespace McpServer;

using Common;
using Common.Helpers;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PaddleOcr;
using SharpHook;
using SharpHook.Data;
using System.ComponentModel;
using System.Drawing;

public record OcrResult(Rectangle Box, string Text, float Score);

[McpServerToolType]
public class WindowTools(WindowMonitor monitor,
                         PaddleOcrEngine engine,
                         IEventSimulator simulator,
                         ILogger<WindowTools> logger)
{
    private void RequireWindowForeground()
    {
        if (!SpinWait.SpinUntil(() => monitor.IsForeground, 4000))
        {
            throw new TimeoutException("等待窗口超时");
        }
    }

    [McpServerTool]
    [Description("窗口OCR结果。返回包含{Box:{X,Y,W,H},Text,Score}的数组")]
    public OcrResult[] DetectText()
    {
        RequireWindowForeground();

        using var image = monitor.Screenshot;
        if (image == null)
        {
            return [];
        }

        var results = engine.DetectAndRecognize(image, [new(0, 0, image.Width, image.Height)]);
        var ocrResults = results.Select(x => new OcrResult(x.Det.Box, x.Rec.Text, x.Rec.Score)).ToArray();

        logger.LogDebug("{} ...", string.Join("\n", ocrResults.Take(3)));
        return ocrResults;
    }

    [McpServerTool]
    [Description("移动鼠标到指定位置并点击")]
    public void MouseClick(MouseButton button, short x, short y)
    {
        RequireWindowForeground();

        var cb = monitor.ClientBounds;
        var mapper = new CoordinateMapper
        {
            Source = new(0, 0, monitor.ClientBounds.Width, monitor.ClientBounds.Height),
            Target = cb
        };
        var pt = mapper.Map(new Point(x, y));

        logger.LogDebug("输入 ({},{}), 转换: ({},{})", x, y, pt.X, pt.Y);

        if (cb.Left <= pt.X && pt.X <= cb.Right && cb.Top <= pt.Y && pt.Y <= cb.Bottom)
        {
            simulator.SimulateMouseMovement((short)pt.X, (short)pt.Y);
            simulator.SimulateMousePress(button);
            simulator.SimulateMouseRelease(button);
        }
        else
        {
            throw new ArgumentOutOfRangeException($"{x},{y} 位置超出窗口范围");
        }
    }

    [McpServerTool]
    [Description("按下并释放键盘按键")]
    public void KeyClick(KeyCode code)
    {
        RequireWindowForeground();

        simulator.SimulateKeyPress(code);
        simulator.SimulateKeyRelease(code);
    }
}
