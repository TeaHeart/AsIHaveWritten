# OmniParser 重构设计

## 背景

当前 `OmniParser` 同时负责 YOLO 图标检测、PaddleOCR 文本识别、OCR/YOLO 结果合并和标注图生成。目标是将 `OmniParser` 收敛为独立的目标识别项目，使它不再依赖 `PaddleOcr`，并由 `McpServer.ParseScreen()` 负责组合 YOLO 与 OCR 结果。

## 目标

- `OmniParser` 不再引用 `PaddleOcr`。
- `OmniParserEngine` 对外 API 改为 `Detect(...)`，只返回目标检测结果。
- `OmniParser` 改为 `Exe` 项目，提供独立 demo 与 benchmark。
- `McpServer.ParseScreen()` 调用 `OmniParser` 和 `PaddleOcr`，并在 MCP 层合并结果。
- MCP 对外 `ParseScreen()` 返回结构保持不变。

## 非目标

- 不调整 YOLO 阈值、NMS 阈值或检测行为。
- 不调整 PaddleOCR 行为。
- 不改变 MCP 工具对外返回字段。
- 不引入额外配置系统或新中间项目。

## 编码规范

- 新增或重写的文本文件使用 UTF-8 with BOM 编码。
- 新增或重写的文本文件使用 CRLF 换行。
- C# 文件继续遵循 `.editorconfig`：4 空格缩进、file-scoped namespace、using 放在 namespace 内部、`insert_final_newline = false`。

## 架构边界

重构后依赖关系为：

```text
McpServer
  ├─ OmniParser
  └─ PaddleOcr

OmniParser
  └─ Common
```

`OmniParser` 只管理 YOLO ONNX session、预处理、推理、后处理，并返回检测框。`McpServer` 作为应用层组合点，负责截图、YOLO 检测、OCR 识别、结果合并和 MCP 输出映射。

## OmniParser API

`OmniParserEngine` 改为以下 public API：

```csharp
public sealed class OmniParserEngine : IDisposable
{
    public IReadOnlyList<DetectedElement> Detect(
        ReadOnlySpan<byte> image,
        ReadOnlySpan<long> imageShape);

    public IReadOnlyList<DetectedElement> Detect(Bitmap bitmap);
}
```

`imageShape` 与 `Common.Extensions.BitmapExtensions.ToBytes()` 保持一致，表示 `[height, width, channels]`。

检测结果模型：

```csharp
namespace OmniParser.Models;

public record DetectedElement(
    Rectangle Box,
    string Type,
    float Score
);
```

初期 `Type` 固定为 `"icon"`。`Box` 使用原图坐标，`Score` 使用 YOLO confidence。

## OmniParser 内部拆分

保留 `YoloDetector` 作为 YOLO 预处理和后处理组件，但将输入从 `Bitmap` 调整为 `ReadOnlySpan<byte>` + `imageShape`，风格对齐 `PaddleOcr`。

```text
OmniParser/
  OmniParserEngine.cs        // Detect API + YOLO session 管理
  YoloDetector.cs            // letterbox、tensor 填充、后处理、NMS
  Program.cs                 // demo + benchmark
  Models/
    DetectedElement.cs       // public 检测结果
```

删除或迁移：

```text
OmniParser/Models/ParsedElement.cs  -> 删除
OmniParser/BoxMerger.cs             -> 迁移到 McpServer
OmniParser/BoxAnnotator.cs          -> 删除，demo 使用 Common.DrawBoxes
```

## OmniParser demo 与 benchmark

`OmniParser.csproj` 改为：

```xml
<OutputType>Exe</OutputType>
```

新增 `Program.cs`，参考 `PaddleOcr/Program.cs`：

1. 加载测试图片。
2. `bitmap.ToBytes(out imageShape)`。
3. 调用 `engine.Detect(image, imageShape)`。
4. 输出每个检测框的 `Score`、`Box`、`Type`。
5. 使用 `bitmap.DrawBoxes(...)` 绘制检测框。
6. 使用 `bitmap.Show("boxes", false)` 展示结果。
7. 使用 `Benchmark("Detect", ...)` 循环测量检测耗时。

测试图片可以从 `PaddleOcr/Resources/ppocr/test*.png` 复制到 `OmniParser/Resources/imgs/`，供独立 demo 使用。

## McpServer 数据流

`WindowTools.ParseScreen()` 保持 MCP 对外入口不变，内部流程改为：

```text
RequireWindowForeground
  -> monitor.Screenshot
  -> omniParser.Detect(image)
  -> paddleOcr.DetectAndRecognize(image, full-screen region)
  -> ScreenElementMerger.Merge(yoloResults, ocrResults)
  -> map to OmniParseResult[]
```

示意代码：

```csharp
var regions = new[] { new Rectangle(0, 0, image.Width, image.Height) };
var yoloResults = omniParser.Detect(image);
var ocrResults = engine.DetectAndRecognize(image, regions)
    .Select(r => new OcrResult(r.Det.Box, r.Rec.Text, r.Rec.Score))
    .ToArray();
var elements = ScreenElementMerger.Merge(yoloResults, ocrResults);
```

`OmniParseResult` 对外结构保持：

```csharp
public record OmniParseResult(
    int Index,
    string Type,
    Rectangle Box,
    string? Content,
    bool Interactivity);
```

## 合并逻辑迁移

在 `McpServer` 中新增内部模型：

```csharp
internal record ScreenElement(
    Rectangle Box,
    string Type,
    string? Content,
    bool Interactivity,
    float Score
);
```

新增 `ScreenElementMerger`，迁移当前 `BoxMerger` 行为：

1. OCR 结果先作为 `text` 元素加入。
2. YOLO 检测结果按是否包含 OCR 文本排序。
3. 若 OCR 框与 icon 框的重叠比例大于 `0.8`，OCR 文本合并为 icon 的 `Content`，对应 OCR 元素从独立文本结果中移除。
4. icon 元素 `Interactivity = true`。
5. text 元素 `Interactivity = false`。

迁移时移除当前未使用的 `imageSize` 参数。

## 错误处理与资源释放

保持现有错误行为：

- YOLO 模型路径错误或 ONNX session 初始化失败，由 `OmniParserEngine` 构造阶段抛出。
- OCR 模型路径错误，由 `PaddleOcrEngine` 抛出。
- `monitor.Screenshot == null` 时，`ParseScreen()` 返回空数组。
- 合并逻辑不吞异常，不添加额外 fallback。

资源释放：

- `OmniParserEngine.Dispose()` 只释放 YOLO session。
- `PaddleOcrEngine.Dispose()` 保持现状。
- `ParseScreen()` 中截图仍使用 `using var image = monitor.Screenshot`。
- `Detect(Bitmap)` 只是便利重载，内部转为 Span API。

## 验证

- 运行 `dotnet build AsIHaveWritten.sln`。
- 确认 `OmniParser.csproj` 不再引用 `PaddleOcr.csproj`。
- 确认 `OmniParser` 可作为 Exe 构建，并包含 demo/benchmark。
- 确认 `McpServer.ParseScreen()` 在 MCP 层组合 YOLO 与 OCR。
- 如本地模型和图片资源完整，可运行 OmniParser demo 检查检测框输出和 benchmark 输出。

## 成功标准

- `OmniParser` 是独立目标检测 Exe 项目。
- `OmniParserEngine.Detect(...)` 支持 Span 与 Bitmap 两种入口。
- `OmniParser` 不依赖 `PaddleOcr`。
- OCR/YOLO 合并逻辑位于 `McpServer`。
- MCP `ParseScreen()` 对外返回字段保持不变。