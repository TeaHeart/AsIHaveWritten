namespace McpServer;

using Common;
using Common.Helpers;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OmniParser;
using PaddleOcr;
using SharpHook;
using SharpHook.Data;
using System.ComponentModel;
using System.Drawing;

public readonly record struct OcrResult(Rectangle Box, string Text, float Score);
public readonly record struct OmniResult(Rectangle Box, float Score);

[McpServerToolType]
public class WindowTools(WindowMonitor monitor,
                         PaddleOcrEngine engine,
                         OmniParserEngine omniParser,
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

        logger.LogDebug("{}", string.Join("\n", ocrResults.AsEnumerable()));
        return ocrResults;
    }

    [McpServerTool]
    [Description("解析当前窗口界面，返回所有UI元素（按钮、图标、文本）的结构化列表")]
    public ScreenElement[] ParseScreen()
    {
        RequireWindowForeground();

        using var image = monitor.Screenshot;
        if (image == null)
        {
            return [];
        }

        var regions = new[] { new Rectangle(0, 0, image.Width, image.Height) };
        var iconResults = omniParser.Detect(image)
            .Select(x => new OmniResult(x.Box, x.Score))
            .ToArray();
        var ocrResults = engine.DetectAndRecognize(image, regions)
            .Select(x => new OcrResult(x.Det.Box, x.Rec.Text, x.Rec.Score))
            .ToArray();
        var elements = ScreenElementMerger.Merge(iconResults, ocrResults).ToArray();

        logger.LogDebug("{}", string.Join("\n", elements.AsEnumerable()));
        return elements;
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

        logger.LogDebug("点击: {}", code);

        simulator.SimulateKeyPress(code);
        simulator.SimulateKeyRelease(code);
    }
}