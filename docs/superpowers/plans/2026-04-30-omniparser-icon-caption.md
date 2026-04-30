# OmniParser Icon Caption Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `OmniParser` 中接入 Florence2 icon caption ONNX pipeline，并让 `McpServer.ParseScreen()` 用 caption 补足无文字图标语义。

**Architecture:** `OmniParserEngine` 保持现有 `Detect(...)` API 不变，新增 `Caption(...)` 与 `DetectAndCaption(...)` named tuple API；`DetectAndCaption(...)` 只组合 `Detect + Caption`。新建 `Captioner` 管理 Florence2 tokenizer、预处理、ONNX sessions 与 greedy decoder loop；验证不新增单元测试项目，全部通过 `OmniParser/Program.cs` 的 demo、smoke check 与 benchmark 完成。

**Tech Stack:** C#/.NET 8 Windows, Microsoft.ML.OnnxRuntime.DirectML, Microsoft.ML.Tokenizers, System.Drawing.Common, existing `Common.Extensions.BitmapExtensions.ToBytes()` image convention.

**Coding Standard:** 新增或完整重写的文本文件使用 UTF-8 with BOM 与 CRLF。C# 文件遵循 `.editorconfig`：4 空格缩进、file-scoped namespace、`using` 放在 namespace 内部、`insert_final_newline = false`。

---

## File Structure

### Create

- `OmniParser/Captioner.cs` — internal Florence2 icon caption implementation: crop/pad/resize/normalize, tokenizer wrapper, ONNX session runs, greedy decode, caption cleanup.
- `OmniParser/Resources/omni/icon_caption/onnx/*.onnx` — runtime Florence2 ONNX assets copied from `.claude/OmniParser-v2.0_icon_caption/onnx/`.
- `OmniParser/Resources/omni/icon_caption/*.json` and `merges.txt` — tokenizer, preprocessor, generation, and model config assets copied from `.claude/OmniParser-v2.0_icon_caption/`.

### Modify

- `OmniParser/OmniParser.csproj` — add production package `Microsoft.ML.Tokenizers`; keep existing `Resources\**` copy rule.
- `OmniParser/OmniParserEngine.cs` — add lazy `Captioner`, Span/Bitmap `Caption(...)`, Span/Bitmap `DetectAndCaption(...)`, and dispose caption resources.
- `OmniParser/Program.cs` — add tokenizer/preprocess smoke output, caption demo output, and benchmarks for `Detect`, `Caption`, `DetectAndCaption`.
- `McpServer/WindowTools.cs` — extend `OmniResult` with `string? Caption`; make `ParseScreen()` use `bitmap.ToBytes(out imageShape)`, `DetectAndCaption(image, imageShape)`, and OCR Span overload.
- `McpServer/ScreenElementMerger.cs` — use contained OCR text first; when no OCR text is contained, use `OmniResult.Caption`.

### Do Not Create

- 不创建 `AsIHaveWritten.Tests` 或任何 xUnit/NUnit/MSTest 项目。
- 不新增 `InternalsVisibleTo`。
- 不把 tokenizer/preprocess/merge 验证写成单元测试；只通过 `OmniParser/Program.cs` 和构建验证。

### Do Not Modify

- `OmniParser/Detector.cs` — YOLO thresholds, NMS, box semantics, and current `Detect(...)` behavior remain unchanged.
- `PaddleOcr/PaddleOcrEngine.cs` — OCR behavior remains unchanged; only call its existing Span overload from MCP.

---

## Task 1: Add runtime assets and production dependency

**Files:**
- Modify: `OmniParser/OmniParser.csproj`
- Create: `OmniParser/Resources/omni/icon_caption/onnx/*.onnx`
- Create: `OmniParser/Resources/omni/icon_caption/{vocab.json,merges.txt,added_tokens.json,special_tokens_map.json,preprocessor_config.json,generation_config.json,config.json}`

- [ ] **Step 1: Copy Florence2 icon caption runtime assets**

Run from repository root:

```bash
mkdir -p OmniParser/Resources/omni/icon_caption
cp -R .claude/OmniParser-v2.0_icon_caption/onnx OmniParser/Resources/omni/icon_caption/
cp .claude/OmniParser-v2.0_icon_caption/vocab.json OmniParser/Resources/omni/icon_caption/
cp .claude/OmniParser-v2.0_icon_caption/merges.txt OmniParser/Resources/omni/icon_caption/
cp .claude/OmniParser-v2.0_icon_caption/added_tokens.json OmniParser/Resources/omni/icon_caption/
cp .claude/OmniParser-v2.0_icon_caption/special_tokens_map.json OmniParser/Resources/omni/icon_caption/
cp .claude/OmniParser-v2.0_icon_caption/preprocessor_config.json OmniParser/Resources/omni/icon_caption/
cp .claude/OmniParser-v2.0_icon_caption/generation_config.json OmniParser/Resources/omni/icon_caption/
cp .claude/OmniParser-v2.0_icon_caption/config.json OmniParser/Resources/omni/icon_caption/
```

Expected: `OmniParser/Resources/omni/icon_caption/onnx/vision_encoder.onnx` and `OmniParser/Resources/omni/icon_caption/vocab.json` exist.

- [ ] **Step 2: Add `Microsoft.ML.Tokenizers` package**

Run:

```bash
dotnet add OmniParser/OmniParser.csproj package Microsoft.ML.Tokenizers
```

Expected: `OmniParser/OmniParser.csproj` gains a concrete `PackageReference` for `Microsoft.ML.Tokenizers` with the NuGet version chosen by `dotnet add`.

- [ ] **Step 3: Build OmniParser after dependency/resource setup**

Run:

```bash
dotnet build OmniParser/OmniParser.csproj
```

Expected: PASS. This confirms adding the package and large resources did not break the existing `Detect(...)` executable.

- [ ] **Step 4: Commit this setup slice if commits are requested**

Only if the user explicitly requests commits:

```bash
git add OmniParser/OmniParser.csproj OmniParser/Resources/omni/icon_caption
git commit -m "feat: add OmniParser icon caption assets"
```

---

## Task 2: Create `Captioner` preprocessing and tokenizer foundation

**Files:**
- Create: `OmniParser/Captioner.cs`

- [ ] **Step 1: Create `Captioner.cs` with preprocessing, cleanup, and tokenizer wrapper**

Create `OmniParser/Captioner.cs`:

```csharp
namespace OmniParser;

using Microsoft.ML.Tokenizers;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

internal sealed partial class Captioner : IDisposable
{
    internal const int ImageSize = 768;
    private const float PaddingRatio = 0.2f;

    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];
    private static readonly Regex SpecialTokenRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public void Dispose()
    {
    }

    internal static float[] Preprocess(ReadOnlySpan<byte> image, ReadOnlySpan<long> imageShape, Rectangle box)
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
            throw new ArgumentException("Captioning requires at least 3 image channels.", nameof(imageShape));
        }

        var expectedLength = checked(width * height * channels);
        if (image.Length < expectedLength)
        {
            throw new ArgumentException("image buffer is smaller than imageShape requires.", nameof(image));
        }

        var cropBox = ClampAndPadBox(box, width, height);
        using var source = CreateBitmapFromPackedRgb(image, width, height, channels);
        using var crop = source.Clone(cropBox, PixelFormat.Format24bppRgb);
        using var resized = new Bitmap(ImageSize, ImageSize, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.DrawImage(crop, new Rectangle(0, 0, ImageSize, ImageSize));
        }

        return CopyBitmapToNormalizedNchw(resized);
    }

    internal static string? CleanCaption(string raw)
    {
        var cleaned = SpecialTokenRegex.Replace(raw, string.Empty).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static Rectangle ClampAndPadBox(Rectangle box, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp(box.Left, 0, imageWidth);
        var top = Math.Clamp(box.Top, 0, imageHeight);
        var right = Math.Clamp(box.Right, 0, imageWidth);
        var bottom = Math.Clamp(box.Bottom, 0, imageHeight);

        if (right <= left || bottom <= top)
        {
            return new Rectangle(0, 0, imageWidth, imageHeight);
        }

        var width = right - left;
        var height = bottom - top;
        var padX = (int)MathF.Round(width * PaddingRatio);
        var padY = (int)MathF.Round(height * PaddingRatio);

        var paddedLeft = Math.Clamp(left - padX, 0, imageWidth - 1);
        var paddedTop = Math.Clamp(top - padY, 0, imageHeight - 1);
        var paddedRight = Math.Clamp(right + padX, paddedLeft + 1, imageWidth);
        var paddedBottom = Math.Clamp(bottom + padY, paddedTop + 1, imageHeight);

        return Rectangle.FromLTRB(paddedLeft, paddedTop, paddedRight, paddedBottom);
    }

    private static Bitmap CreateBitmapFromPackedRgb(ReadOnlySpan<byte> image, int width, int height, int channels)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var bitmapData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        try
        {
            var rowLength = checked(width * 3);
            var sourceRowLength = checked(width * channels);
            var rowBuffer = new byte[rowLength];
            for (var y = 0; y < height; y++)
            {
                image.Slice(y * sourceRowLength, rowLength).CopyTo(rowBuffer);
                Marshal.Copy(rowBuffer, 0, bitmapData.Scan0 + y * bitmapData.Stride, rowLength);
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return bitmap;
    }

    private static float[] CopyBitmapToNormalizedNchw(Bitmap bitmap)
    {
        var output = new float[1 * 3 * ImageSize * ImageSize];
        var bitmapData = bitmap.LockBits(new Rectangle(0, 0, ImageSize, ImageSize), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            var rowBuffer = new byte[bitmapData.Stride];
            for (var y = 0; y < ImageSize; y++)
            {
                Marshal.Copy(bitmapData.Scan0 + y * bitmapData.Stride, rowBuffer, 0, bitmapData.Stride);
                for (var x = 0; x < ImageSize; x++)
                {
                    var pixelOffset = x * 3;
                    var targetOffset = y * ImageSize + x;
                    output[0 * ImageSize * ImageSize + targetOffset] = (rowBuffer[pixelOffset + 0] / 255f - Mean[0]) / Std[0];
                    output[1 * ImageSize * ImageSize + targetOffset] = (rowBuffer[pixelOffset + 1] / 255f - Mean[1]) / Std[1];
                    output[2 * ImageSize * ImageSize + targetOffset] = (rowBuffer[pixelOffset + 2] / 255f - Mean[2]) / Std[2];
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return output;
    }

    internal sealed class FlorenceTokenizer
    {
        private static readonly IReadOnlyDictionary<string, int> BaseSpecialTokens = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["<s>"] = 0,
            ["<pad>"] = 1,
            ["</s>"] = 2,
            ["<unk>"] = 3,
            ["<mask>"] = 50264
        };

        private readonly BpeTokenizer _tokenizer;

        public FlorenceTokenizer(string modelDirectory)
        {
            var vocabPath = Path.Combine(modelDirectory, "vocab.json");
            var mergesPath = Path.Combine(modelDirectory, "merges.txt");
            var addedTokensPath = Path.Combine(modelDirectory, "added_tokens.json");
            var specialTokens = LoadSpecialTokens(addedTokensPath);

            var options = new BpeOptions(vocabPath, mergesPath)
            {
                ByteLevel = true,
                PreTokenizer = RobertaPreTokenizer.Instance,
                UnknownToken = "<unk>",
                SpecialTokens = specialTokens
            };

            _tokenizer = BpeTokenizer.Create(options);
        }

        public IReadOnlyList<int> EncodePrompt(string prompt)
        {
            return _tokenizer.EncodeToIds(prompt);
        }

        public string? Decode(IReadOnlyList<int> tokenIds)
        {
            var decoded = _tokenizer.Decode(tokenIds, considerSpecialTokens: false);
            return CleanCaption(decoded);
        }

        private static IReadOnlyDictionary<string, int> LoadSpecialTokens(string addedTokensPath)
        {
            var tokens = new Dictionary<string, int>(BaseSpecialTokens, StringComparer.Ordinal);
            using var stream = File.OpenRead(addedTokensPath);
            var added = JsonSerializer.Deserialize<Dictionary<string, int>>(stream)
                ?? throw new InvalidOperationException($"The content of '{addedTokensPath}' is not valid.");

            foreach (var (token, id) in added)
            {
                tokens[token] = id;
            }

            return tokens;
        }
    }
}
```

- [ ] **Step 2: Add Program smoke checks for tokenizer, caption cleanup, and preprocessing**

Temporarily add this helper to `OmniParser/Program.cs` after `Main` and call it near the start of `Main` after `image` is created:

```csharp
        SmokeCheckCaptionFoundation(image, imageShape);
```

Helper:

```csharp
    static void SmokeCheckCaptionFoundation(byte[] image, long[] imageShape)
    {
        var tokenizer = new Captioner.FlorenceTokenizer("Resources/omni/icon_caption");
        var promptIds = tokenizer.EncodePrompt("<cap>");
        Console.WriteLine($"Tokenizer <cap>: {string.Join(",", promptIds)}");
        Console.WriteLine($"CleanCaption: {Captioner.CleanCaption("<cap> settings </cap>")}");

        var pixels = Captioner.Preprocess(image, imageShape, new Rectangle(0, 0, 32, 32));
        Console.WriteLine($"Preprocess: len={pixels.Length}, first={pixels[0]:F4}, min={pixels.Min():F4}, max={pixels.Max():F4}");
    }
```

Expected smoke output when `dotnet run --project OmniParser/OmniParser.csproj` later runs:

```text
Tokenizer <cap>: 51269
CleanCaption: settings
Preprocess: len=1769472, first=..., min=..., max=...
```

- [ ] **Step 3: Build OmniParser**

Run:

```bash
dotnet build OmniParser/OmniParser.csproj
```

Expected: PASS.

- [ ] **Step 4: Commit foundation slice if commits are requested**

Only if the user explicitly requests commits:

```bash
git add OmniParser/Captioner.cs OmniParser/Program.cs
git commit -m "feat: add icon caption foundation"
```

---

## Task 3: Implement `Captioner` ONNX sessions and greedy decoder

**Files:**
- Modify: `OmniParser/Captioner.cs`

- [ ] **Step 1: Add ONNX Runtime fields, constructor, and public caption method**

Add `using Microsoft.ML.OnnxRuntime;` to `Captioner.cs` and add these fields/constructor/methods inside `Captioner` before the static preprocessing methods:

```csharp
    private const string CaptionPrompt = "<cap>";
    private const int DecoderStartTokenId = 2;
    private const int EosTokenId = 2;
    private const int MaxNewTokens = 20;
    private const int HiddenSize = 768;
    private const int VocabSize = 51289;

    private readonly InferenceSession? _visionEncoder;
    private readonly InferenceSession? _embedTokens;
    private readonly InferenceSession? _encoder;
    private readonly InferenceSession? _decoder;
    private readonly InferenceSession? _decoderWithPast;
    private readonly FlorenceTokenizer? _tokenizer;

    public Captioner(string modelDirectory, SessionOptions options)
    {
        var onnxDirectory = Path.Combine(modelDirectory, "onnx");
        _visionEncoder = new InferenceSession(Path.Combine(onnxDirectory, "vision_encoder.onnx"), options);
        _embedTokens = new InferenceSession(Path.Combine(onnxDirectory, "embed_tokens.onnx"), options);
        _encoder = new InferenceSession(Path.Combine(onnxDirectory, "encoder_model.onnx"), options);
        _decoder = new InferenceSession(Path.Combine(onnxDirectory, "decoder_model.onnx"), options);
        _decoderWithPast = new InferenceSession(Path.Combine(onnxDirectory, "decoder_with_past_model.onnx"), options);
        _tokenizer = new FlorenceTokenizer(modelDirectory);
    }

    public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> Caption(
        ReadOnlySpan<byte> image,
        ReadOnlySpan<long> imageShape,
        IReadOnlyList<(Rectangle Box, float Score)> boxes)
    {
        if (boxes.Count == 0)
        {
            return [];
        }

        var tokenizer = _tokenizer ?? throw new ObjectDisposedException(nameof(Captioner));
        var results = new List<(Rectangle Box, float Score, string? Caption)>(boxes.Count);
        foreach (var box in boxes)
        {
            var pixels = Preprocess(image, imageShape, box.Box);
            var tokenIds = GenerateCaptionTokenIds(pixels);
            var caption = tokenizer.Decode(tokenIds);
            results.Add((box.Box, box.Score, caption));
        }

        return results;
    }
```

Replace `Dispose()` with:

```csharp
    public void Dispose()
    {
        _visionEncoder?.Dispose();
        _embedTokens?.Dispose();
        _encoder?.Dispose();
        _decoder?.Dispose();
        _decoderWithPast?.Dispose();
    }
```

- [ ] **Step 2: Add tensor creation and ONNX run helpers**

Add these helpers inside `Captioner`:

```csharp
    private sealed class OnnxRunResult : IDisposable
    {
        private readonly IDisposableReadOnlyCollection<OrtValue> _values;

        public OnnxRunResult(IReadOnlyList<string> outputNames, IDisposableReadOnlyCollection<OrtValue> values)
        {
            _values = values;
            ByName = outputNames.Select((name, index) => (name, value: values[index]))
                .ToDictionary(x => x.name, x => x.value, StringComparer.Ordinal);
        }

        public IReadOnlyDictionary<string, OrtValue> ByName { get; }

        public OrtValue this[string name] => ByName[name];

        public void Dispose()
        {
            _values.Dispose();
        }
    }

    private static OrtValue CreateFloatTensor(float[] values, long[] shape)
    {
        return OrtValue.CreateTensorValueFromMemory(values, shape);
    }

    private static OrtValue CreateInt64Tensor(long[] values, long[] shape)
    {
        return OrtValue.CreateTensorValueFromMemory(values, shape);
    }

    private static OnnxRunResult Run(InferenceSession session, IReadOnlyDictionary<string, OrtValue> inputs)
    {
        var inputNames = inputs.Keys.ToArray();
        var inputValues = inputs.Values.ToArray();
        var outputNames = session.OutputNames.ToArray();
        var values = session.Run(null, inputNames, inputValues, outputNames);
        return new OnnxRunResult(outputNames, values);
    }

    private static float[] ReadFloatTensor(OrtValue value)
    {
        return value.GetTensorDataAsSpan<float>().ToArray();
    }
```

If the installed ONNX Runtime package exposes a different disposable collection type for `Run(...)`, keep the ownership pattern: one wrapper owns the disposable output collection; named `OrtValue`s are only read while that wrapper is alive.

- [ ] **Step 3: Implement encoder helpers**

Add these helpers inside `Captioner`:

```csharp
    private OnnxRunResult RunVisionEncoder(float[] pixelValues)
    {
        var session = _visionEncoder ?? throw new ObjectDisposedException(nameof(Captioner));
        using var input = CreateFloatTensor(pixelValues, [1, 3, ImageSize, ImageSize]);
        return Run(session, new Dictionary<string, OrtValue>(StringComparer.Ordinal)
        {
            ["pixel_values"] = input
        });
    }

    private OnnxRunResult RunEmbedTokens(IReadOnlyList<int> tokenIds)
    {
        var session = _embedTokens ?? throw new ObjectDisposedException(nameof(Captioner));
        var ids = tokenIds.Select(id => (long)id).ToArray();
        using var input = CreateInt64Tensor(ids, [1, tokenIds.Count]);
        return Run(session, new Dictionary<string, OrtValue>(StringComparer.Ordinal)
        {
            ["input_ids"] = input
        });
    }

    private OnnxRunResult RunTextEncoder(float[] imageFeatures, int imageSequenceLength, float[] promptEmbeds, int promptSequenceLength)
    {
        var session = _encoder ?? throw new ObjectDisposedException(nameof(Captioner));
        var encoderSequenceLength = imageSequenceLength + promptSequenceLength;
        var inputsEmbeds = new float[encoderSequenceLength * HiddenSize];
        imageFeatures.CopyTo(inputsEmbeds, 0);
        promptEmbeds.CopyTo(inputsEmbeds, imageFeatures.Length);

        var attentionMask = Enumerable.Repeat(1L, encoderSequenceLength).ToArray();
        using var embedsOrt = CreateFloatTensor(inputsEmbeds, [1, encoderSequenceLength, HiddenSize]);
        using var maskOrt = CreateInt64Tensor(attentionMask, [1, encoderSequenceLength]);

        return Run(session, new Dictionary<string, OrtValue>(StringComparer.Ordinal)
        {
            ["inputs_embeds"] = embedsOrt,
            ["attention_mask"] = maskOrt
        });
    }
```

- [ ] **Step 4: Implement decoder helpers**

Add these helpers inside `Captioner`:

```csharp
    private OnnxRunResult RunDecoderFirstStep(OrtValue encoderHiddenStates)
    {
        var session = _decoder ?? throw new ObjectDisposedException(nameof(Captioner));
        using var startEmbedding = RunEmbedTokens([DecoderStartTokenId]);
        return Run(session, new Dictionary<string, OrtValue>(StringComparer.Ordinal)
        {
            ["encoder_hidden_states"] = encoderHiddenStates,
            ["inputs_embeds"] = startEmbedding["inputs_embeds"]
        });
    }

    private OnnxRunResult RunDecoderWithPast(int tokenId, IReadOnlyDictionary<string, OrtValue> previousPresent)
    {
        var session = _decoderWithPast ?? throw new ObjectDisposedException(nameof(Captioner));
        using var tokenEmbedding = RunEmbedTokens([tokenId]);
        var inputs = new Dictionary<string, OrtValue>(StringComparer.Ordinal)
        {
            ["inputs_embeds"] = tokenEmbedding["inputs_embeds"]
        };

        foreach (var inputName in session.InputNames.Where(name => name.StartsWith("past_key_values.", StringComparison.Ordinal)))
        {
            var presentName = "present." + inputName["past_key_values.".Length..];
            inputs[inputName] = previousPresent[presentName];
        }

        return Run(session, inputs);
    }

    private static int ArgMaxLastToken(ReadOnlySpan<float> logits)
    {
        if (logits.Length < VocabSize)
        {
            throw new InvalidOperationException($"Decoder logits length {logits.Length} is smaller than vocabulary size {VocabSize}.");
        }

        var offset = logits.Length - VocabSize;
        var bestToken = 0;
        var bestValue = logits[offset];
        for (var i = 1; i < VocabSize; i++)
        {
            var value = logits[offset + i];
            if (value > bestValue)
            {
                bestValue = value;
                bestToken = i;
            }
        }

        return bestToken;
    }
```

- [ ] **Step 5: Implement `GenerateCaptionTokenIds` orchestration**

Add this method inside `Captioner`:

```csharp
    private IReadOnlyList<int> GenerateCaptionTokenIds(float[] pixelValues)
    {
        var tokenizer = _tokenizer ?? throw new ObjectDisposedException(nameof(Captioner));
        using var vision = RunVisionEncoder(pixelValues);
        var imageFeatures = ReadFloatTensor(vision["image_features"]);
        var imageSequenceLength = imageFeatures.Length / HiddenSize;

        var promptIds = tokenizer.EncodePrompt(CaptionPrompt);
        using var prompt = RunEmbedTokens(promptIds);
        var promptEmbeds = ReadFloatTensor(prompt["inputs_embeds"]);
        var promptSequenceLength = promptIds.Count;

        using var encoder = RunTextEncoder(imageFeatures, imageSequenceLength, promptEmbeds, promptSequenceLength);
        using var first = RunDecoderFirstStep(encoder["last_hidden_state"]);

        var generated = new List<int>(MaxNewTokens);
        var nextToken = ArgMaxLastToken(first["logits"].GetTensorDataAsSpan<float>());
        if (nextToken == EosTokenId)
        {
            return generated;
        }

        generated.Add(nextToken);
        OnnxRunResult? previous = first;
        OnnxRunResult? current = null;

        try
        {
            for (var i = 1; i < MaxNewTokens; i++)
            {
                current = RunDecoderWithPast(nextToken, previous.ByName);
                nextToken = ArgMaxLastToken(current["logits"].GetTensorDataAsSpan<float>());
                if (!ReferenceEquals(previous, first))
                {
                    previous.Dispose();
                }

                previous = current;
                current = null;

                if (nextToken == EosTokenId)
                {
                    break;
                }

                generated.Add(nextToken);
            }
        }
        finally
        {
            current?.Dispose();
            if (previous is not null && !ReferenceEquals(previous, first))
            {
                previous.Dispose();
            }
        }

        return generated;
    }
```

- [ ] **Step 6: Build OmniParser to catch ONNX Runtime API mismatches**

Run:

```bash
dotnet build OmniParser/OmniParser.csproj
```

Expected: PASS. If the installed `Microsoft.ML.OnnxRuntime.DirectML` exposes a different disposable collection type for `Run(...)`, adjust only helper signatures around `OnnxRunResult`; do not change model data flow or public APIs.

- [ ] **Step 7: Commit ONNX caption core if commits are requested**

Only if the user explicitly requests commits:

```bash
git add OmniParser/Captioner.cs
git commit -m "feat: add Florence ONNX caption generation"
```

---

## Task 4: Extend `OmniParserEngine` with `Caption` and `DetectAndCaption`

**Files:**
- Modify: `OmniParser/OmniParserEngine.cs`

- [ ] **Step 1: Modify fields and constructor for lazy captioner**

Update `OmniParser/OmniParserEngine.cs` so the class fields and constructor are:

```csharp
    private readonly InferenceSession _det;
    private readonly SessionOptions _options;
    private readonly string _captionModelDirectory;
    private Captioner? _captioner;

    public OmniParserEngine(string detModelPath = "Resources/omni/icon_detect.onnx",
                            string captionModelDirectory = "Resources/omni/icon_caption",
                            SessionOptions? options = null)
    {
        if (options == null)
        {
            options = new SessionOptions();
            options.AppendExecutionProvider_DML();
            options.AppendExecutionProvider_CPU();
        }

        _options = options;
        _captionModelDirectory = captionModelDirectory;
        _det = new InferenceSession(detModelPath, options);
    }
```

This keeps `Detect(...)` behavior unchanged because caption sessions are created only when `Caption(...)` or `DetectAndCaption(...)` is called.

- [ ] **Step 2: Add caption API methods**

Add these methods after existing `Detect(...)` overloads:

```csharp
    public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> Caption(
        ReadOnlySpan<byte> image,
        ReadOnlySpan<long> imageShape,
        IReadOnlyList<(Rectangle Box, float Score)> boxes)
    {
        _captioner ??= new Captioner(_captionModelDirectory, _options);
        return _captioner.Caption(image, imageShape, boxes);
    }

    public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> Caption(
        Bitmap bitmap,
        IReadOnlyList<(Rectangle Box, float Score)> boxes)
    {
        return Caption(bitmap.ToBytes(out var imageShape), imageShape, boxes);
    }

    public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> DetectAndCaption(
        ReadOnlySpan<byte> image,
        ReadOnlySpan<long> imageShape)
    {
        var boxes = Detect(image, imageShape);
        return Caption(image, imageShape, boxes);
    }

    public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> DetectAndCaption(Bitmap bitmap)
    {
        return DetectAndCaption(bitmap.ToBytes(out var imageShape), imageShape);
    }
```

- [ ] **Step 3: Dispose lazy captioner**

Replace `Dispose()` with:

```csharp
    public void Dispose()
    {
        _captioner?.Dispose();
        _det.Dispose();
    }
```

- [ ] **Step 4: Build OmniParser**

Run:

```bash
dotnet build OmniParser/OmniParser.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit engine API slice if commits are requested**

Only if the user explicitly requests commits:

```bash
git add OmniParser/OmniParserEngine.cs
git commit -m "feat: expose icon caption APIs"
```

---

## Task 5: Update MCP merge behavior with OCR priority and caption fallback

**Files:**
- Modify: `McpServer/WindowTools.cs`
- Modify: `McpServer/ScreenElementMerger.cs`

- [ ] **Step 1: Extend `OmniResult` and update `ParseScreen()` data flow**

In `McpServer/WindowTools.cs`, add:

```csharp
using Common.Extensions;
```

Replace:

```csharp
public readonly record struct OmniResult(Rectangle Box, float Score);
```

with:

```csharp
public readonly record struct OmniResult(Rectangle Box, float Score, string? Caption);
```

In `ParseScreen()`, replace the body after the null screenshot check with:

```csharp
        var imageBytes = image.ToBytes(out var imageShape);
        var regions = new[] { new Rectangle(0, 0, image.Width, image.Height) };
        var iconResults = omniParser.DetectAndCaption(imageBytes, imageShape)
            .Select(x => new OmniResult(x.Box, x.Score, x.Caption))
            .ToArray();
        var ocrResults = engine.DetectAndRecognize(imageBytes, imageShape, regions)
            .Select(x => new OcrResult(x.Det.Box, x.Rec.Text, x.Rec.Score))
            .ToArray();
        var elements = ScreenElementMerger.Merge(iconResults, ocrResults).ToArray();

        logger.LogDebug("{}", string.Join("\n", elements.AsEnumerable()));
        return elements;
```

- [ ] **Step 2: Update `ScreenElementMerger` caption fallback**

In `McpServer/ScreenElementMerger.cs`, replace the `foreach` icon loop with:

```csharp
        foreach (var item in iconResults.OrderBy(element => BoxContainsAnyOcr(element.Box, ocrResults) ? 1 : 0))
        {
            var box = item.Box;
            var containedOcr = elements
                .Where(e => e.Type == "text" && OverlapRatio(e.Box, box) > 0.8)
                .ToList();

            var content = item.Caption;
            if (containedOcr.Count > 0)
            {
                content = string.Join(" ", containedOcr.Select(e => e.Content));
                foreach (var ocr in containedOcr)
                {
                    elements.Remove(ocr);
                }
            }

            elements.Add(new("icon", box, content, item.Score));
        }
```

- [ ] **Step 3: Build McpServer**

Run:

```bash
dotnet build McpServer/McpServer.csproj
```

Expected: PASS.

- [ ] **Step 4: Commit MCP merge slice if commits are requested**

Only if the user explicitly requests commits:

```bash
git add McpServer/WindowTools.cs McpServer/ScreenElementMerger.cs
git commit -m "feat: merge icon captions into parsed screen elements"
```

---

## Task 6: Add OmniParser caption demo, smoke checks, and benchmarks

**Files:**
- Modify: `OmniParser/Program.cs`

- [ ] **Step 1: Replace `Program.Main` demo flow**

Replace `Main` in `OmniParser/Program.cs` with:

```csharp
    static void Main(string[] args)
    {
        using var bitmap = new Bitmap("Resources/omni/test4.png");
        using var engine = new OmniParserEngine();

        var image = bitmap.ToBytes(out var imageShape);
        SmokeCheckCaptionFoundation(image, imageShape);

        var boxes = engine.Detect(image, imageShape);
        Console.WriteLine("Detect:");
        foreach (var box in boxes)
        {
            Console.WriteLine($"{box.Score:F4}\t{box.Box,-40}");
        }

        var captions = engine.Caption(image, imageShape, boxes);
        Console.WriteLine();
        Console.WriteLine("Caption:");
        foreach (var item in captions)
        {
            Console.WriteLine($"{item.Score:F4}\t{item.Box,-40}\t{item.Caption}");
        }

        var detectAndCaptions = engine.DetectAndCaption(image, imageShape);
        Console.WriteLine();
        Console.WriteLine("DetectAndCaption:");
        foreach (var item in detectAndCaptions)
        {
            Console.WriteLine($"{item.Score:F4}\t{item.Box,-40}\t{item.Caption}");
        }

        bitmap.DrawBoxes(boxes.Select(x => (x.Box, x.Score)));
        bitmap.Show("boxes", false);

        Benchmark("Detect", () =>
        {
            _ = engine.Detect(image, imageShape);
        });

        Benchmark("Caption", () =>
        {
            _ = engine.Caption(image, imageShape, boxes);
        });

        Benchmark("DetectAndCaption", () =>
        {
            _ = engine.DetectAndCaption(image, imageShape);
        });
    }
```

- [ ] **Step 2: Ensure `SmokeCheckCaptionFoundation` is present**

Keep this helper in `Program.cs`:

```csharp
    static void SmokeCheckCaptionFoundation(byte[] image, long[] imageShape)
    {
        var tokenizer = new Captioner.FlorenceTokenizer("Resources/omni/icon_caption");
        var promptIds = tokenizer.EncodePrompt("<cap>");
        Console.WriteLine($"Tokenizer <cap>: {string.Join(",", promptIds)}");
        Console.WriteLine($"CleanCaption: {Captioner.CleanCaption("<cap> settings </cap>")}");

        var pixels = Captioner.Preprocess(image, imageShape, new Rectangle(0, 0, 32, 32));
        Console.WriteLine($"Preprocess: len={pixels.Length}, first={pixels[0]:F4}, min={pixels.Min():F4}, max={pixels.Max():F4}");
        Console.WriteLine();
    }
```

- [ ] **Step 3: Build OmniParser**

Run:

```bash
dotnet build OmniParser/OmniParser.csproj
```

Expected: PASS.

- [ ] **Step 4: Run OmniParser demo as the primary validation**

Run:

```bash
dotnet run --project OmniParser/OmniParser.csproj
```

Expected console sections:

```text
Tokenizer <cap>: 51269
CleanCaption: settings
Preprocess: len=1769472, first=..., min=..., max=...

Detect:
Caption:
DetectAndCaption:
              Detect	...
             Caption	...
    DetectAndCaption	...
```

Acceptance checks:

- `Tokenizer <cap>` prints `51269`, proving the Florence added token is loaded.
- `Preprocess` prints length `1769472` (`1 * 3 * 768 * 768`) and finite min/max values.
- `Detect` prints score and box rows.
- `Caption` prints the same box count as `Detect`; each row preserves `Score` and `Box` and prints caption text or blank `null`.
- `DetectAndCaption` prints rows and benchmarks include `Detect`, `Caption`, and `DetectAndCaption`.

- [ ] **Step 5: Commit demo slice if commits are requested**

Only if the user explicitly requests commits:

```bash
git add OmniParser/Program.cs
git commit -m "demo: show icon captions in OmniParser"
```

---

## Task 7: Run final build and Program-only validation

**Files:**
- Read/verify only: changed source files from previous tasks

- [ ] **Step 1: Run solution build**

Run:

```bash
dotnet build AsIHaveWritten.sln
```

Expected: PASS.

- [ ] **Step 2: Run Program-only validation**

Run:

```bash
dotnet run --project OmniParser/OmniParser.csproj
```

Expected: PASS through the smoke checks, demo sections, and benchmark output described in Task 6.

- [ ] **Step 3: Inspect public API shape**

Confirm `OmniParser/OmniParserEngine.cs` contains exactly these public method groups in addition to `Dispose()`:

```csharp
public IReadOnlyList<(Rectangle Box, float Score)> Detect(ReadOnlySpan<byte> image, ReadOnlySpan<long> imageShape)
public IReadOnlyList<(Rectangle Box, float Score)> Detect(Bitmap bitmap)
public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> Caption(ReadOnlySpan<byte> image, ReadOnlySpan<long> imageShape, IReadOnlyList<(Rectangle Box, float Score)> boxes)
public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> Caption(Bitmap bitmap, IReadOnlyList<(Rectangle Box, float Score)> boxes)
public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> DetectAndCaption(ReadOnlySpan<byte> image, ReadOnlySpan<long> imageShape)
public IReadOnlyList<(Rectangle Box, float Score, string? Caption)> DetectAndCaption(Bitmap bitmap)
```

Expected: method names, tuple element names, and overload shapes match the design spec.

- [ ] **Step 4: Confirm MCP merge source code rule**

Inspect `McpServer/ScreenElementMerger.cs` and confirm the icon content rule is:

```text
contained OCR text -> icon.Content
else icon.Caption -> icon.Content
else null
```

Expected: caption never overrides contained OCR text.

- [ ] **Step 5: Commit final validation adjustments if commits are requested**

Only if the user explicitly requests commits:

```bash
git add OmniParser McpServer
git commit -m "feat: integrate OmniParser icon caption"
```

---

## Self-Review Checklist

- Spec coverage: dependency strategy, model assets, `Captioner`, tokenizer, preprocessing, greedy ONNX generation, engine API, MCP data flow, merge semantics, demo, benchmarks, and build validation are each mapped to tasks above.
- User correction applied: no unit test project, no xUnit, no `InternalsVisibleTo`; validation happens in `OmniParser/Program.cs` plus `dotnet build`.
- No public result record was added; public caption results use named tuples as requested.
- No new production dependency beyond `Microsoft.ML.Tokenizers` was introduced.
- `Detect(...)` remains unchanged and caption sessions are lazy so pure detection callers do not initialize caption sessions.
- Thread safety is not handled inside `Captioner` or `OmniParserEngine`; callers remain responsible for serialization or instance isolation.
- OCR text has priority over caption in `ScreenElementMerger`; caption only fills icons without contained OCR text.
