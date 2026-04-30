# OmniParser icon_caption 接入设计

## 背景

当前 `OmniParser` 已使用 `Resources/omni/icon_detect.onnx` 完成 UI 图标检测，`McpServer.ParseScreen()` 再把 `OmniParser` 的 icon 检测结果与 `PaddleOcr` 的 OCR 结果合并为 `ScreenElement[]`。`.claude/OmniParser-v2.0_icon_caption/onnx` 中已经存在 Florence2 icon caption ONNX 模型，可以在 C# 中通过 ONNX Runtime 补齐“无文字图标”的语义描述。

## 目标

- 在 `OmniParser` 中接入 icon caption ONNX pipeline。
- 保持现有 `Detect(...)` API 与行为不变。
- 新增 `Caption(...)` 与 `DetectAndCaption(...)`，风格与现有 `Detect(...)`、`PaddleOcrEngine` 的 Span/Bitmap 重载保持一致。
- `McpServer.ParseScreen()` 使用 `bitmap.ToBytes(out imageShape)` 后调用 `DetectAndCaption(image, imageShape)`，再与 OCR 结果合并。
- `Program.cs` 增加 caption demo 和 benchmark，便于独立验证 `Detect`、`Caption`、`DetectAndCaption` 的效果与耗时。

## 非目标

- 不改动 YOLO icon detection 的阈值、NMS 或输出框语义。
- 不引入 Python sidecar 作为运行时依赖。
- 不引入 `Microsoft.ML.Transforms`、`Microsoft.ML.ImageAnalytics`、`Microsoft.ML.OnnxTransformer`。
- 不在 `Captioner` 或 `OmniParserEngine` 内部处理线程安全；调用方负责串行化或实例隔离。
- 不在第一版实现 beam search；第一版使用 greedy decoding 打通链路。
- 不改变 MCP 对外字段结构，只扩展 icon 的 `Content` 来源。

## 依赖策略

新增生产依赖只包含 `Microsoft.ML.Tokenizers`。实现时用 `dotnet add OmniParser package Microsoft.ML.Tokenizers` 添加 NuGet 当前稳定版本，并让生成的 `PackageReference` 在 `OmniParser.csproj` 中固定实际版本号。

继续使用现有依赖：

- `Microsoft.ML.OnnxRuntime.DirectML`：直接管理 `InferenceSession` 与 `OrtValue`。
- `System.Drawing.Common`：继续使用 `Bitmap`、crop、resize。
- `Common.Extensions.BitmapExtensions.ToBytes()`：维持项目现有 `[height, width, channels]` 图像输入约定。

不采用 ML.NET pipeline 相关库的原因：

- `Microsoft.ML.ImageAnalytics` 的 `ResizeImages` / `ExtractPixels` 是 `IDataView` pipeline 风格，和当前直接构造 ONNX tensor 的风格不一致；Florence2 还需要按通道 mean/std normalize，手写更直接、更容易与 Python golden baseline 对齐。
- `Microsoft.ML.OnnxTransformer.ApplyOnnxModel` 适合单次静态 ONNX scoring，不适合 Florence2 的多 ONNX session + decoder loop + `past_key_values` 管理。
- `Microsoft.ML.Transforms` 不提供本接入需要的生成式模型推理能力。

## 模型资产

icon caption 使用 `.claude/OmniParser-v2.0_icon_caption` 下的文件作为源资产：

```text
.claude/OmniParser-v2.0_icon_caption/
  onnx/
    vision_encoder.onnx
    embed_tokens.onnx
    encoder_model.onnx
    decoder_model.onnx
    decoder_with_past_model.onnx
  vocab.json
  merges.txt
  added_tokens.json
  special_tokens_map.json
  preprocessor_config.json
  generation_config.json
  config.json
```

实现时需要把运行时所需文件复制或映射到 `OmniParser/Resources/omni/icon_caption/`，并通过 `.csproj` 的 `Resources\**` 复制规则进入输出目录。

## 对外 API

`OmniParserEngine` 保留现有 API：

```csharp
public IReadOnlyList<(Rectangle Box, float Score)> Detect(
    ReadOnlySpan<byte> image,
    ReadOnlySpan<long> imageShape);

public IReadOnlyList<(Rectangle Box, float Score)> Detect(Bitmap bitmap);
```

新增 caption API：

```csharp
public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> Caption(
    ReadOnlySpan<byte> image,
    ReadOnlySpan<long> imageShape,
    IReadOnlyList<(Rectangle Box, float Score)> boxes);

public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> Caption(
    Bitmap bitmap,
    IReadOnlyList<(Rectangle Box, float Score)> boxes);

public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> DetectAndCaption(
    ReadOnlySpan<byte> image,
    ReadOnlySpan<long> imageShape);

public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> DetectAndCaption(Bitmap bitmap);
```

`DetectAndCaption(...)` 只是组合方法：

```text
DetectAndCaption(image, imageShape)
  -> boxes = Detect(image, imageShape)
  -> return Caption(image, imageShape, boxes)
```

`Caption(...)` 的作用是方便单独测试 caption 模型：调用方可以传入固定 boxes，避免每次验证 caption 都重复跑检测。

## 内部结构

不额外设计接口，不新增 public result record。新增内部类 `Captioner`，风格靠近当前 `Detector`：

```text
OmniParser/
  OmniParserEngine.cs     // 管理 icon detect session 和 caption sessions；暴露 Detect/Caption/DetectAndCaption
  Detector.cs             // 现有 YOLO 预处理、后处理、NMS
  Captioner.cs            // 新增 Florence2 预处理、后处理和私有推理方法
  Program.cs              // demo + benchmark
  Resources/omni/
    icon_detect.onnx
    icon_caption/...
```

`Captioner` 主要包含：

```text
Captioner
  Preprocess(...)
  Postprocess(...)
  private CropAndPad(...)
  private ResizeAndNormalize(...)
  private EncodePrompt(...)
  private RunVisionEncoder(...)
  private RunEmbedTokens(...)
  private RunTextEncoder(...)
  private RunDecoderFirstStep(...)
  private RunDecoderWithPastLoop(...)
  private DecodeTokens(...)
```

`Captioner` 可以是 `internal sealed class`，持有 tokenizer 与多个 ONNX sessions；也可以拆为静态预处理/后处理 + `OmniParserEngine` 持有 sessions。实现时优先选择代码更清晰、资源释放更直接的形态。

## Captioner 预处理

输入：

```text
ReadOnlySpan<byte> image
ReadOnlySpan<long> imageShape = [height, width, channels]
IReadOnlyList<(Rectangle Box, float Score)> boxes
```

单个 box 的预处理流程：

```text
box
  -> clamp 到原图范围
  -> 按比例扩边 padding，避免只截到图标边缘
  -> 从原图 bytes 裁剪为 Bitmap 或中间 RGB buffer
  -> resize 到 768x768
  -> convert RGB
  -> value / 255
  -> normalize:
       R = (R - 0.485) / 0.229
       G = (G - 0.456) / 0.224
       B = (B - 0.406) / 0.225
  -> 输出 float[1, 3, 768, 768]
```

`768x768`、mean、std 来自 `preprocessor_config.json`。输出使用 NCHW，与 `vision_encoder.onnx` 的 `pixel_values` 输入匹配。

## Tokenizer 设计

新增薄 wrapper，例如 `FlorenceTokenizer`，内部使用 `Microsoft.ML.Tokenizers.BpeTokenizer`：

```text
FlorenceTokenizer
  EncodePrompt(string prompt) -> IReadOnlyList<int>
  Decode(IReadOnlyList<int> tokenIds) -> string
```

配置来源：

- `vocab.json`
- `merges.txt`
- `added_tokens.json`
- `special_tokens_map.json`

建议配置：

```text
BpeOptions.ByteLevel = true
BpeOptions.PreTokenizer = RobertaPreTokenizer.Instance
BpeOptions.UnknownToken = "<unk>"
BpeOptions.SpecialTokens = added_tokens.json + known base special tokens
```

第一版 prompt 固定为 caption 任务。实际使用 `<CAPTION>` 对应的任务提示，还是直接使用 tokenizer 中的 `<cap>` 相关 special token，需要通过 Python baseline 确认。实现前先用 golden test 固定 prompt token ids，避免 C# 与 Python tokenizer 行为偏差。

解码时跳过生成结果中的特殊 token，并对输出做最小清理：trim 空白、移除残留任务 token、空字符串返回 `null`。

## Florence2 ONNX 推理流程

单个 crop caption 流程：

```text
pixel_values
  -> vision_encoder.onnx
       input:  pixel_values [1,3,768,768]
       output: image_features [1,image_sequence_length,768]

prompt
  -> FlorenceTokenizer.EncodePrompt
  -> embed_tokens.onnx
       input:  input_ids [1,prompt_sequence_length]
       output: inputs_embeds [1,prompt_sequence_length,768]

image_features + prompt inputs_embeds
  -> encoder_model.onnx
       input:  inputs_embeds [1,encoder_sequence_length,768]
       input:  attention_mask [1,encoder_sequence_length]
       output: last_hidden_state [1,encoder_sequence_length,768]

first decoder token
  -> embed_tokens.onnx
  -> decoder_model.onnx
       input:  encoder_hidden_states
       input:  inputs_embeds
       output: logits + present.*

next tokens
  -> embed_tokens.onnx
  -> decoder_with_past_model.onnx 循环
       input:  inputs_embeds
       input:  past_key_values.*
       output: logits + updated present.*
```

第一版 generation：

- 使用 greedy decoding：每步取最后位置 logits 最大的 token id。
- `decoder_start_token_id = 2`，按 `generation_config.json` / `config.json` 验证。
- 遇到 `eos_token_id = 2` 停止。
- `maxNewTokens` 默认 20，与配置中的 `max_length` 对齐。
- 不实现 `num_beams = 3`，除非后续质量验证证明 greedy 不够。

## McpServer 数据流

`WindowTools.ParseScreen()` 调整为：

```text
ParseScreen
  -> RequireWindowForeground
  -> bitmap = monitor.Screenshot
  -> image = bitmap.ToBytes(out imageShape)
  -> iconResults = omniParser.DetectAndCaption(image, imageShape)
  -> ocrResults = paddleOcr.DetectAndRecognize(image, imageShape, fullWindowRegion)
  -> ScreenElementMerger.Merge(iconResults, ocrResults)
  -> ScreenElement[]
```

`OmniResult` 扩展为：

```csharp
public readonly record struct OmniResult(
    Rectangle Box,
    float Score,
    string? Caption
);
```

`ScreenElementMerger` 合并规则：

```text
1. OCR 结果先作为 text 元素加入。
2. icon 按现有规则与 OCR 结果做包含/重叠判断。
3. 若 icon 包含 OCR text：
     icon.Content = 合并后的 OCR text
     被吸收的 OCR text 从独立 text 元素中移除
4. 若 icon 没有包含 OCR text：
     icon.Content = icon.Caption
5. 若 OCR text 和 Caption 都没有：
     icon.Content = null
```

这样 caption 只补足无文字图标，不覆盖 OCR 识别到的按钮文字。

## Program.cs demo 与 benchmark

`OmniParser/Program.cs` 增加：

```text
1. 加载测试图片。
2. image = bitmap.ToBytes(out imageShape)。
3. boxes = engine.Detect(image, imageShape)。
4. captions = engine.Caption(image, imageShape, boxes)。
5. detectAndCaptions = engine.DetectAndCaption(image, imageShape)。
6. 输出 Score、Box、Caption。
7. DrawBoxes 展示检测框。
8. benchmark:
   - Detect
   - Caption
   - DetectAndCaption
```

`Caption` benchmark 使用已经检测好的 `boxes`，避免重复计入 detect 时间。

## 错误处理与资源释放

- 模型路径错误、ONNX session 初始化失败、tokenizer 文件缺失：构造阶段抛出异常，不吞掉。
- `Caption(...)` 对空 boxes 返回空列表。
- 单个 caption 输出为空字符串时返回 `null`。
- `DetectAndCaption(...)` 中 `Detect` 抛出的异常继续向外传播。
- 不在 `Captioner` 内部添加 fallback 到 Python 或 OCR。
- `OmniParserEngine.Dispose()` 释放 detection session 与 caption 相关 sessions。
- 线程安全由调用方负责；`Captioner` 不加锁。

## 测试方案

### 1. FlorenceTokenizer golden test

目的：确认 `Microsoft.ML.Tokenizers` 配置与 Python Florence2 tokenizer 一致。

测试内容：

- 固定 caption prompt。
- Python baseline 生成 prompt token ids。
- C# `FlorenceTokenizer.EncodePrompt(...)` 生成 token ids。
- 比较 token ids 完全一致。
- 对固定 generated token ids，比较 C# decode 与 Python decode 的清理后文本一致。

### 2. Captioner.Preprocess golden test

目的：确认图像预处理与 Python `Florence2Processor` 对齐。

测试内容：

- 固定图片和固定 box。
- Python baseline 输出 `pixel_values`。
- C# `Captioner.Preprocess(...)` 输出 `float[1,3,768,768]`。
- 比较 shape 完全一致。
- 比较采样点、min/max/mean，浮点误差控制在 `1e-4` 量级。

### 3. Caption(...) 集成测试

目的：不依赖检测模型，单独验证 caption 链路。

输入：

```text
bitmap 或 image/imageShape + 固定 boxes
```

断言：

- 返回数量与 boxes 数量一致。
- 每项保留原始 `Box` 与 `Score`。
- 空 boxes 返回空列表。
- caption 为空时返回 `null`，非空时是清理后的文本。

### 4. DetectAndCaption(...) 集成测试

目的：验证组合方法等价于 `Detect + Caption`。

断言：

- `DetectAndCaption(image, imageShape)` 的 `Box` / `Score` 与 `Detect(image, imageShape)` 对齐。
- `Caption` 数量与 `Detect` boxes 数量一致。
- Bitmap 重载和 Span 重载结果一致。

### 5. Program demo 验证

运行 `OmniParser` demo，确认：

- `Detect` 输出 score 和 box。
- `Caption` 输出 score、box、caption。
- `DetectAndCaption` 输出 score、box、caption。
- benchmark 包含 `Detect`、`Caption`、`DetectAndCaption`。

### 6. 构建验证

运行：

```text
dotnet build AsIHaveWritten.sln
```

确认新增 `Microsoft.ML.Tokenizers` 后解决方案可构建。

## 风险与缓解

### 1. Florence2 decoder loop 复杂

风险：`past_key_values` 的输入输出 shape、首轮 decoder 与后续 decoder 的切换容易出错。

缓解：第一版只做 greedy decoding，并为 `Caption(...)` 提供固定 boxes 测试入口，先验证单 crop caption 链路。

### 2. Tokenizer 与 Python 不一致

风险：ByteLevel BPE、special tokens、prompt template 细节不一致会导致模型输出异常。

缓解：使用 `Microsoft.ML.Tokenizers`，并以 Python baseline 做 token ids golden test；prompt 第一版固定，不暴露为可配置项。

### 3. 图像 crop/padding 影响 caption 质量

风险：box 太紧缺上下文，box 太宽引入其他 UI 元素。

缓解：`Captioner` 内部采用固定扩边策略并 clamp 到原图；通过 `Caption(...)` 使用固定 boxes 反复比较不同策略。

### 4. 性能成本高

风险：caption 需要多模型推理和 decoder 循环，明显慢于 icon detection。

缓解：`Program.cs` benchmark 分开测量 `Detect`、`Caption`、`DetectAndCaption`；后续可基于数据再决定是否限制 caption 数、提高 score 阈值、增加缓存或 batch。

### 5. 模型文件体积大

风险：ONNX 模型文件会显著增加资源体积和启动加载时间。

缓解：设计中明确资源目录和复制规则；实现时检查 Git LFS/资源分发策略，不把 `.claude` 临时目录作为运行时依赖。

## 成功标准

- `OmniParserEngine` 同时提供 `Detect`、`Caption`、`DetectAndCaption` 的 Span 与 Bitmap 重载。
- `DetectAndCaption` 内部明确复用 `Detect + Caption`。
- `Captioner` 使用 ONNX Runtime + `Microsoft.ML.Tokenizers` 完成 C# 原生 icon caption。
- `McpServer.ParseScreen()` 的数据流为 `bitmap -> ToBytes -> DetectAndCaption -> OCR -> merge`。
- `ScreenElementMerger` 对 icon 内容采用 OCR 优先、caption 补充的规则。
- `Program.cs` 包含 caption demo 与 `Detect` / `Caption` / `DetectAndCaption` benchmark。
- 解决方案构建通过，核心 golden tests 能证明 tokenizer 和 preprocess 与 Python baseline 对齐。
