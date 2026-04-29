# OmniParser Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor `OmniParser` into an independent YOLO target-detection executable and move OCR/YOLO merge orchestration into `McpServer.ParseScreen()`.

**Architecture:** `OmniParser` owns only YOLO model loading, image preprocessing, inference, postprocessing, and demo benchmarking. `McpServer` remains the application composition layer: it captures screenshots, invokes `OmniParserEngine.Detect(...)`, invokes `PaddleOcrEngine.DetectAndRecognize(...)`, merges both result sets, and maps to the existing MCP output shape.

**Tech Stack:** C#/.NET 8 Windows, Microsoft.ML.OnnxRuntime.DirectML, System.Drawing.Common, existing `Common.Extensions.BitmapExtensions`, existing `PaddleOcrEngine` Span-style API.

**Coding Standard:** New or fully rewritten text files must be saved as UTF-8 with BOM and CRLF line endings. C# files must follow `.editorconfig`: 4-space indentation, file-scoped namespaces, `using` directives inside the namespace, and `insert_final_newline = false`.

---

## File Structure

### Create

All created or fully rewritten text files in this plan must be written as UTF-8 with BOM and CRLF line endings.

- `OmniParser/Models/DetectedElement.cs` — public YOLO detection result model exposed by `OmniParser`.
- `OmniParser/Program.cs` — standalone demo and benchmark, modeled after `PaddleOcr/Program.cs`.
- `McpServer/ScreenElement.cs` — internal merged UI element model used before mapping to `OmniParseResult`.
- `McpServer/ScreenElementMerger.cs` — OCR/YOLO merge logic migrated from `OmniParser/BoxMerger.cs`.
- `OmniParser/Resources/imgs/test1.png` — copied from `PaddleOcr/Resources/ppocr/test1.png`.
- `OmniParser/Resources/imgs/test2.png` — copied from `PaddleOcr/Resources/ppocr/test2.png`.
- `OmniParser/Resources/imgs/test3.png` — copied from `PaddleOcr/Resources/ppocr/test3.png`.
- `OmniParser/Resources/imgs/test4.png` — copied from `PaddleOcr/Resources/ppocr/test4.png`.

### Modify

- `OmniParser/OmniParser.csproj` — change to `Exe`, remove `PaddleOcr` project reference, keep `Common`, copy resources.
- `OmniParser/OmniParserEngine.cs` — replace `ParseScreen(...)` with `Detect(...)` overloads and remove OCR/session merge responsibilities.
- `OmniParser/YoloDetector.cs` — change preprocessing input from `Bitmap` to `ReadOnlySpan<byte>` + `imageShape`, return data needed for postprocess without storing it in `Bitmap.Tag`.
- `McpServer/Program.cs` — construct `OmniParserEngine` with only `icon_detect.onnx`.
- `McpServer/WindowTools.cs` — compose YOLO + OCR results and use `ScreenElementMerger`.

### Delete

- `OmniParser/BoxMerger.cs` — logic moves to `McpServer/ScreenElementMerger.cs`.
- `OmniParser/BoxAnnotator.cs` — no longer used; demo uses `Common.Extensions.BitmapExtensions.DrawBoxes(...)`.
- `OmniParser/Models/ParsedElement.cs` — no longer part of pure detection API.

---

## Task 1: Convert OmniParser to pure detection project metadata and result model

**Files:**
- Modify: `OmniParser/OmniParser.csproj`
- Create: `OmniParser/Models/DetectedElement.cs`
- Delete later, not in this task: `OmniParser/Models/ParsedElement.cs`

- [ ] **Step 1: Inspect current project file**

Read `OmniParser/OmniParser.csproj` and confirm it currently contains:

```xml
<OutputType>Library</OutputType>
<ProjectReference Include="..\PaddleOcr\PaddleOcr.csproj" />
```

- [ ] **Step 2: Change OmniParser project to Exe and remove PaddleOcr reference**

Edit `OmniParser/OmniParser.csproj` to this complete content:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.ML.OnnxRuntime.DirectML" Version="1.23.0" />
    <PackageReference Include="System.Drawing.Common" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Common\Common.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="Resources\**">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create detected result model**

Create `OmniParser/Models/DetectedElement.cs`:

```csharp
namespace OmniParser.Models;

using System.Drawing;

public record DetectedElement(
    Rectangle Box,
    string Type,
    float Score
);
```

- [ ] **Step 4: Build OmniParser and record expected temporary failure**

Run:

```bash
dotnet build OmniParser/OmniParser.csproj
```

Expected: FAIL because `OmniParserEngine.cs` still references `PaddleOcr`, `ParsedElement`, and no `Program.Main` may exist after converting to Exe. This failure confirms the project metadata change exposed the remaining refactor work.

- [ ] **Step 5: Commit this metadata/model slice if commits are requested**

Only if the user explicitly requested commits, run:

```bash
git add OmniParser/OmniParser.csproj OmniParser/Models/DetectedElement.cs
git commit -m "refactor: make OmniParser a detection executable"
```

---

## Task 2: Refactor YOLO preprocessing to Span-style input

**Files:**
- Modify: `OmniParser/YoloDetector.cs`

- [ ] **Step 1: Replace `YoloDetector.cs` with Span-based implementation**

Replace the complete contents of `OmniParser/YoloDetector.cs` with:

```csharp
namespace OmniParser;

using Microsoft.ML.OnnxRuntime;
using System.Drawing;

internal static class YoloDetector
{
    private const int InputSize = 640;
    private const float ConfThreshold = 0.05f;
    private const float NmsThreshold = 0.5f;

    private static readonly long[] OutputShape = [1, 5, 8400];
    private static readonly int OutputLength = 5 * 8400;

    public readonly record struct DetectedBox(Rectangle Box, float Confidence);

    public readonly record struct PreprocessResult(
        OrtValue Input,
        OrtValue Output,
        float[] OutputBuffer,
        float PadW,
        float PadH,
        float Scale,
        Size OriginalSize
    ) : IDisposable
    {
        public void Dispose()
        {
            Input.Dispose();
            Output.Dispose();
        }
    }

    public static PreprocessResult Preprocess(ReadOnlySpan<byte> image, ReadOnlySpan<long> imageShape)
    {
        if (imageShape.Length < 3)
        {
            throw new ArgumentException("imageShape must contain height, width, and channels.", nameof(imageShape));
        }

        var height = checked((int)imageShape[0]);
        var width = checked((int)imageShape[1]);
        var channels = checked((int)imageShape[2]);
        if (channels < 3)
        {
            throw new ArgumentException("YOLO detection requires at least 3 image channels.", nameof(imageShape));
        }

        var (padW, padH, scale) = ComputeLetterbox(width, height, InputSize, InputSize);
        var inputShape = new long[] { 1, 3, InputSize, InputSize };
        var inputArray = new float[InputSize * InputSize * 3];
        FillLetterboxInput(image, width, height, channels, inputArray, padW, padH, scale);

        var outputBuffer = new float[OutputLength];
        var inputOrt = OrtValue.CreateTensorValueFromMemory(inputArray, inputShape);
        var outputOrt = OrtValue.CreateTensorValueFromMemory(outputBuffer, OutputShape);

        return new PreprocessResult(
            inputOrt,
            outputOrt,
            outputBuffer,
            padW,
            padH,
            scale,
            new Size(width, height));
    }

    public static List<DetectedBox> Postprocess(ReadOnlySpan<float> output, PreprocessResult input)
    {
        var numDetections = 8400;
        var candidates = new List<DetectedBox>(numDetections);

        for (int i = 0; i < numDetections; i++)
        {
            var cx = output[0 * numDetections + i];
            var cy = output[1 * numDetections + i];
            var w = output[2 * numDetections + i];
            var h = output[3 * numDetections + i];
            var conf = output[4 * numDetections + i];

            if (conf < ConfThreshold)
            {
                continue;
            }

            var x1 = (cx - w / 2 - input.PadW) / input.Scale;
            var y1 = (cy - h / 2 - input.PadH) / input.Scale;
            var x2 = (cx + w / 2 - input.PadW) / input.Scale;
            var y2 = (cy + h / 2 - input.PadH) / input.Scale;

            x1 = Math.Clamp(x1, 0, input.OriginalSize.Width);
            y1 = Math.Clamp(y1, 0, input.OriginalSize.Height);
            x2 = Math.Clamp(x2, 0, input.OriginalSize.Width);
            y2 = Math.Clamp(y2, 0, input.OriginalSize.Height);

            candidates.Add(new DetectedBox(
                Rectangle.FromLTRB((int)x1, (int)y1, (int)x2, (int)y2),
                conf
            ));
        }

        return NonMaxSuppression(candidates, NmsThreshold);
    }

    private static (float PadW, float PadH, float Scale) ComputeLetterbox(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        var scale = Math.Min((float)targetWidth / sourceWidth, (float)targetHeight / sourceHeight);
        var resizedWidth = (int)(sourceWidth * scale);
        var resizedHeight = (int)(sourceHeight * scale);
        var padW = (targetWidth - resizedWidth) / 2f;
        var padH = (targetHeight - resizedHeight) / 2f;
        return (padW, padH, scale);
    }

    private static void FillLetterboxInput(
        ReadOnlySpan<byte> image,
        int sourceWidth,
        int sourceHeight,
        int channels,
        Span<float> input,
        float padW,
        float padH,
        float scale)
    {
        const byte padColor = 114;
        input.Fill(padColor / 255f);

        var resizedWidth = (int)(sourceWidth * scale);
        var resizedHeight = (int)(sourceHeight * scale);
        var padLeft = (int)padW;
        var padTop = (int)padH;

        for (int y = 0; y < resizedHeight; y++)
        {
            var sourceY = Math.Clamp((int)(y / scale), 0, sourceHeight - 1);
            for (int x = 0; x < resizedWidth; x++)
            {
                var sourceX = Math.Clamp((int)(x / scale), 0, sourceWidth - 1);
                var sourceOffset = (sourceY * sourceWidth + sourceX) * channels;
                var targetX = padLeft + x;
                var targetY = padTop + y;
                var targetOffset = targetY * InputSize + targetX;

                input[0 * InputSize * InputSize + targetOffset] = image[sourceOffset + 0] / 255f;
                input[1 * InputSize * InputSize + targetOffset] = image[sourceOffset + 1] / 255f;
                input[2 * InputSize * InputSize + targetOffset] = image[sourceOffset + 2] / 255f;
            }
        }
    }

    private static List<DetectedBox> NonMaxSuppression(List<DetectedBox> boxes, float iouThreshold)
    {
        if (boxes.Count == 0)
        {
            return [];
        }

        boxes.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        var result = new List<DetectedBox>(boxes.Count);
        var suppressed = new bool[boxes.Count];

        for (int i = 0; i < boxes.Count; i++)
        {
            if (suppressed[i])
            {
                continue;
            }

            result.Add(boxes[i]);

            for (int j = i + 1; j < boxes.Count; j++)
            {
                if (suppressed[j])
                {
                    continue;
                }

                if (IoU(boxes[i].Box, boxes[j].Box) > iouThreshold)
                {
                    suppressed[j] = true;
                }
            }
        }

        return result;
    }

    private static float IoU(Rectangle a, Rectangle b)
    {
        var inter = Rectangle.Intersect(a, b);
        if (inter.Width <= 0 || inter.Height <= 0)
        {
            return 0;
        }

        var interArea = inter.Width * inter.Height;
        var unionArea = a.Width * a.Height + b.Width * b.Height - interArea;
        return unionArea > 0 ? (float)interArea / unionArea : 0;
    }
}
```

- [ ] **Step 2: Build and record expected temporary failure**

Run:

```bash
dotnet build OmniParser/OmniParser.csproj
```

Expected: FAIL because `OmniParserEngine.cs` still calls the old `YoloDetector.Preprocess(Bitmap, out ...)` signature. This confirms the next task must update the engine.

- [ ] **Step 3: Commit this preprocessing slice if commits are requested**

Only if the user explicitly requested commits, run:

```bash
git add OmniParser/YoloDetector.cs
git commit -m "refactor: make YOLO preprocessing span-based"
```

---

## Task 3: Replace OmniParserEngine ParseScreen with Detect overloads

**Files:**
- Modify: `OmniParser/OmniParserEngine.cs`

- [ ] **Step 1: Replace `OmniParserEngine.cs` with pure detection engine**

Replace the complete contents of `OmniParser/OmniParserEngine.cs` with:

```csharp
namespace OmniParser;

using Common.Extensions;
using Microsoft.ML.OnnxRuntime;
using OmniParser.Models;
using System.Drawing;

public sealed class OmniParserEngine : IDisposable
{
    private readonly InferenceSession _yoloSession;
    private readonly string[] _yoloInputNames;
    private readonly string[] _yoloOutputNames;

    public OmniParserEngine(
        string yoloModelPath = "Resources/icon_detect.onnx",
        SessionOptions? options = null)
    {
        options ??= CreateDefaultSessionOptions();

        _yoloSession = new InferenceSession(yoloModelPath, options);
        _yoloInputNames = [_yoloSession.InputNames[0]];
        _yoloOutputNames = [_yoloSession.OutputNames[0]];
    }

    public IReadOnlyList<DetectedElement> Detect(ReadOnlySpan<byte> image, ReadOnlySpan<long> imageShape)
    {
        using var input = YoloDetector.Preprocess(image, imageShape);
        var inputs = new List<OrtValue> { input.Input };
        var outputs = new List<OrtValue> { input.Output };
        _yoloSession.Run(null, _yoloInputNames, inputs, _yoloOutputNames, outputs);

        return YoloDetector.Postprocess(input.OutputBuffer.AsSpan(), input)
            .Select(box => new DetectedElement(box.Box, "icon", box.Confidence))
            .ToArray();
    }

    public IReadOnlyList<DetectedElement> Detect(Bitmap bitmap)
    {
        return Detect(bitmap.ToBytes(out var imageShape), imageShape);
    }

    public void Dispose()
    {
        _yoloSession.Dispose();
    }

    private static SessionOptions CreateDefaultSessionOptions()
    {
        var options = new SessionOptions();
        options.AppendExecutionProvider_DML();
        options.AppendExecutionProvider_CPU();
        return options;
    }
}
```

- [ ] **Step 2: Build OmniParser and record expected temporary failure**

Run:

```bash
dotnet build OmniParser/OmniParser.csproj
```

Expected: FAIL because the project is now `Exe` and `Program.cs` has not been added yet, and old OmniParser-only files may still reference `ParsedElement`. These are resolved in the next tasks.

- [ ] **Step 3: Commit this engine slice if commits are requested**

Only if the user explicitly requested commits, run:

```bash
git add OmniParser/OmniParserEngine.cs
git commit -m "refactor: expose OmniParser Detect API"
```

---

## Task 4: Add OmniParser standalone demo and benchmark

**Files:**
- Create: `OmniParser/Program.cs`
- Create/copy: `OmniParser/Resources/imgs/test1.png`
- Create/copy: `OmniParser/Resources/imgs/test2.png`
- Create/copy: `OmniParser/Resources/imgs/test3.png`
- Create/copy: `OmniParser/Resources/imgs/test4.png`

- [ ] **Step 1: Copy PaddleOCR demo images into OmniParser resources**

Run:

```bash
mkdir -p OmniParser/Resources/imgs && cp PaddleOcr/Resources/ppocr/test*.png OmniParser/Resources/imgs/
```

Expected: `OmniParser/Resources/imgs/test1.png` through `test4.png` exist.

- [ ] **Step 2: Create `OmniParser/Program.cs`**

Create `OmniParser/Program.cs`:

```csharp
namespace OmniParser;

using Common.Extensions;
using System.Diagnostics;
using System.Drawing;

internal class Program
{
    static void Main(string[] args)
    {
        var imagePath = args.Length > 0 ? args[0] : "Resources/imgs/test4.png";
        using var bitmap = new Bitmap(imagePath);
        using var engine = new OmniParserEngine();

        var image = bitmap.ToBytes(out var imageShape);
        var boxes = engine.Detect(image, imageShape);

        foreach (var box in boxes)
        {
            Console.WriteLine($"{box.Score:F4}\t{box.Box,-40}\t{box.Type}");
        }

        bitmap.DrawBoxes(boxes.Select(x => (x.Box, x.Score)));
        bitmap.Show("boxes", false);

        Benchmark("Detect", () =>
        {
            var boxes = engine.Detect(image, imageShape);
        });
    }

    static void Benchmark(string title, Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var times = new List<long>();
        var sw = new Stopwatch();
        var totalTicks = 0L;

        while (totalTicks < TimeSpan.TicksPerSecond)
        {
            sw.Restart();
            action();
            sw.Stop();
            totalTicks += sw.ElapsedTicks;
            times.Add(sw.ElapsedTicks);
        }

        var min = (double)times.Min() / TimeSpan.TicksPerMillisecond;
        var max = (double)times.Max() / TimeSpan.TicksPerMillisecond;
        var avg = (double)times.Average() / TimeSpan.TicksPerMillisecond;
        var sum = (double)times.Sum() / TimeSpan.TicksPerMillisecond;
        Console.WriteLine($"{title,20}\t{times.Count}\tmin:{min:F4}ms\tmax:{max:F4}ms\tavg:{avg:F4}ms\tsum:{sum:F4}ms");
    }
}
```

- [ ] **Step 3: Build OmniParser and record expected temporary failure or success**

Run:

```bash
dotnet build OmniParser/OmniParser.csproj
```

Expected: PASS if `BoxMerger.cs`, `BoxAnnotator.cs`, and `ParsedElement.cs` do not cause compile errors. If it fails because those old files reference removed types or PaddleOCR, continue to Task 5 and delete them.

- [ ] **Step 4: Commit demo slice if commits are requested**

Only if the user explicitly requested commits, run:

```bash
git add OmniParser/Program.cs OmniParser/Resources/imgs/test1.png OmniParser/Resources/imgs/test2.png OmniParser/Resources/imgs/test3.png OmniParser/Resources/imgs/test4.png
git commit -m "feat: add OmniParser detection demo"
```

---

## Task 5: Remove old OmniParser merge and annotation files

**Files:**
- Delete: `OmniParser/BoxMerger.cs`
- Delete: `OmniParser/BoxAnnotator.cs`
- Delete: `OmniParser/Models/ParsedElement.cs`

- [ ] **Step 1: Delete files that no longer belong in OmniParser**

Delete these files:

```text
OmniParser/BoxMerger.cs
OmniParser/BoxAnnotator.cs
OmniParser/Models/ParsedElement.cs
```

- [ ] **Step 2: Verify OmniParser builds**

Run:

```bash
dotnet build OmniParser/OmniParser.csproj
```

Expected: PASS with no `PaddleOcr` dependency and no missing `ParsedElement` references.

- [ ] **Step 3: Verify OmniParser project no longer references PaddleOcr**

Run:

```bash
grep -R "PaddleOcr" OmniParser/OmniParser.csproj OmniParser/*.cs OmniParser/Models/*.cs
```

Expected: no output.

- [ ] **Step 4: Commit cleanup slice if commits are requested**

Only if the user explicitly requested commits, run:

```bash
git add OmniParser/BoxMerger.cs OmniParser/BoxAnnotator.cs OmniParser/Models/ParsedElement.cs
git commit -m "refactor: remove OCR merge from OmniParser"
```

---

## Task 6: Add MCP-side merged element model and merger

**Files:**
- Create: `McpServer/ScreenElement.cs`
- Create: `McpServer/ScreenElementMerger.cs`

- [ ] **Step 1: Create internal merged element model**

Create `McpServer/ScreenElement.cs`:

```csharp
namespace McpServer;

using System.Drawing;

internal record ScreenElement(
    Rectangle Box,
    string Type,
    string? Content,
    bool Interactivity,
    float Score
);
```

- [ ] **Step 2: Create MCP-side merger**

Create `McpServer/ScreenElementMerger.cs`:

```csharp
namespace McpServer;

using OmniParser.Models;
using System.Drawing;

internal static class ScreenElementMerger
{
    public static List<ScreenElement> Merge(
        IReadOnlyList<DetectedElement> detectedElements,
        IReadOnlyList<OcrResult> ocrResults)
    {
        var elements = new List<ScreenElement>();

        foreach (var ocr in ocrResults)
        {
            elements.Add(new ScreenElement(
                ocr.Box,
                "text",
                ocr.Text,
                Interactivity: false,
                ocr.Score
            ));
        }

        var sortedDetectedElements = detectedElements
            .OrderBy(element => BoxContainsAnyOcr(element.Box, ocrResults) ? 1 : 0)
            .ToList();

        foreach (var detected in sortedDetectedElements)
        {
            var box = detected.Box;
            var containedOcr = elements
                .Where(e => e.Type == "text" && OverlapRatio(e.Box, box) > 0.8)
                .ToList();

            string? content = null;
            if (containedOcr.Count > 0)
            {
                content = string.Join(" ", containedOcr.Select(e => e.Content));
                foreach (var ocr in containedOcr)
                {
                    elements.Remove(ocr);
                }
            }

            elements.Add(new ScreenElement(
                box,
                detected.Type,
                content,
                Interactivity: true,
                detected.Score
            ));
        }

        return elements;
    }

    private static bool BoxContainsAnyOcr(Rectangle box, IReadOnlyList<OcrResult> ocrResults)
    {
        return ocrResults.Any(o => OverlapRatio(o.Box, box) > 0.8);
    }

    private static float OverlapRatio(Rectangle inner, Rectangle outer)
    {
        var inter = Rectangle.Intersect(inner, outer);
        if (inter.Width <= 0 || inter.Height <= 0)
        {
            return 0;
        }

        var innerArea = inner.Width * inner.Height;
        if (innerArea <= 0)
        {
            return 0;
        }

        var interArea = inter.Width * inter.Height;
        return (float)interArea / innerArea;
    }
}
```

- [ ] **Step 3: Build McpServer and record expected temporary failure**

Run:

```bash
dotnet build McpServer/McpServer.csproj
```

Expected: FAIL because `McpServer/Program.cs` and `McpServer/WindowTools.cs` still use old `OmniParserEngine` constructor and `ParseScreen(...)` API. The next tasks update those call sites.

- [ ] **Step 4: Commit merger slice if commits are requested**

Only if the user explicitly requested commits, run:

```bash
git add McpServer/ScreenElement.cs McpServer/ScreenElementMerger.cs
git commit -m "refactor: move screen element merge to McpServer"
```

---

## Task 7: Update McpServer DI for pure OmniParserEngine

**Files:**
- Modify: `McpServer/Program.cs`

- [ ] **Step 1: Update OmniParserEngine registration**

In `McpServer/Program.cs`, replace the current `AddSingleton<OmniParserEngine>` block:

```csharp
builder.Services.AddSingleton<OmniParserEngine>(_ =>
{
    var baseDir = AppContext.BaseDirectory;
    return new OmniParserEngine(
        yoloModelPath: Path.Combine(baseDir, "Resources", "icon_detect.onnx"),
        detModelPath: Path.Combine(baseDir, "Resources", "ppocr", "PP-OCRv5_mobile_det_infer.onnx"),
        recModelPath: Path.Combine(baseDir, "Resources", "ppocr", "PP-OCRv5_mobile_rec_infer.onnx"),
        wordDictPath: Path.Combine(baseDir, "Resources", "ppocr", "characterDict.txt"));
});
```

with:

```csharp
builder.Services.AddSingleton<OmniParserEngine>(_ =>
{
    var baseDir = AppContext.BaseDirectory;
    return new OmniParserEngine(
        yoloModelPath: Path.Combine(baseDir, "Resources", "icon_detect.onnx"));
});
```

- [ ] **Step 2: Build McpServer and record expected temporary failure**

Run:

```bash
dotnet build McpServer/McpServer.csproj
```

Expected: FAIL because `WindowTools.ParseScreen()` still calls removed `omniParser.ParseScreen(image)`.

- [ ] **Step 3: Commit DI slice if commits are requested**

Only if the user explicitly requested commits, run:

```bash
git add McpServer/Program.cs
git commit -m "refactor: register pure OmniParser detector"
```

---

## Task 8: Update McpServer ParseScreen orchestration

**Files:**
- Modify: `McpServer/WindowTools.cs`

- [ ] **Step 1: Replace `ParseScreen()` implementation**

In `McpServer/WindowTools.cs`, replace the complete `ParseScreen()` method with:

```csharp
[McpServerTool]
[Description("解析当前窗口界面，返回所有UI元素（按钮、图标、文本）的结构化列表")]
public OmniParseResult[] ParseScreen()
{
    RequireWindowForeground();

    using var image = monitor.Screenshot;
    if (image == null)
    {
        return [];
    }

    var regions = new[] { new Rectangle(0, 0, image.Width, image.Height) };
    var detectedElements = omniParser.Detect(image);
    var ocrResults = engine.DetectAndRecognize(image, regions)
        .Select(x => new OcrResult(x.Det.Box, x.Rec.Text, x.Rec.Score))
        .ToArray();
    var elements = ScreenElementMerger.Merge(detectedElements, ocrResults);

    var items = elements.Select((e, i) => new OmniParseResult(
        Index: i,
        Type: e.Type,
        Box: e.Box,
        Content: e.Content,
        Interactivity: e.Interactivity
    )).ToArray();

    logger.LogDebug("{}", string.Join("\n", items.AsEnumerable()));
    return items;
}
```

- [ ] **Step 2: Build McpServer**

Run:

```bash
dotnet build McpServer/McpServer.csproj
```

Expected: PASS.

- [ ] **Step 3: Commit orchestration slice if commits are requested**

Only if the user explicitly requested commits, run:

```bash
git add McpServer/WindowTools.cs
git commit -m "refactor: merge YOLO and OCR in McpServer"
```

---

## Task 9: Verify full solution and optional demo run

**Files:**
- No direct code changes expected.

- [ ] **Step 1: Build full solution**

Run:

```bash
dotnet build AsIHaveWritten.sln
```

Expected: PASS.

- [ ] **Step 2: Verify OmniParser no longer depends on PaddleOcr**

Use the dedicated search tool if working interactively, or run this shell command if executing outside Claude Code:

```bash
grep -R "PaddleOcr" OmniParser/OmniParser.csproj OmniParser/*.cs OmniParser/Models/*.cs
```

Expected: no output.

- [ ] **Step 3: Run OmniParser demo if local model resources are present**

Run:

```bash
dotnet run --project OmniParser/OmniParser.csproj -- Resources/imgs/test4.png
```

Expected: console output with rows like:

```text
0.1234  {X=...,Y=...,Width=...,Height=...}  icon
              Detect  <count>  min:<n>ms  max:<n>ms  avg:<n>ms  sum:<n>ms
```

The demo also opens a `boxes` image viewer using `BitmapExtensions.Show("boxes", false)`.

- [ ] **Step 4: Check git diff for scope**

Run:

```bash
git diff -- OmniParser McpServer docs/superpowers
```

Expected: changes are limited to the planned refactor, MCP orchestration, copied test images, and spec/plan docs.

- [ ] **Step 5: Final commit if commits are requested**

Only if the user explicitly requested commits and earlier slices were not committed, run:

```bash
git add OmniParser McpServer docs/superpowers
git commit -m "refactor: split OmniParser detection from MCP screen parsing"
```

---

## Self-Review

- Spec coverage: Tasks 1-5 implement `OmniParser` as a pure detection Exe with Span and Bitmap `Detect(...)`; Tasks 6-8 move OCR/YOLO merging into `McpServer`; Task 9 verifies solution build, dependency removal, and demo behavior.
- Placeholder scan: The plan contains no TBD/TODO/fill-in sections. Each code-changing step includes exact file paths and concrete code.
- Type consistency: `DetectedElement(Box, Type, Score)` is defined in Task 1 and consumed by `OmniParserEngine`, `ScreenElementMerger`, and `WindowTools`. `ScreenElement(Box, Type, Content, Interactivity, Score)` is defined before use. `OmniParserEngine.Detect(...)` signatures match all call sites.