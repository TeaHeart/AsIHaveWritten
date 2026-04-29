namespace OmniParser;

using Microsoft.ML.OnnxRuntime;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

/// <summary>
/// Florence-2-base icon captioner using ONNX Runtime.
/// Generates text descriptions for cropped icon regions.
/// </summary>
public sealed class FlorenceCaptioner : IDisposable
{
    private readonly InferenceSession _visionEncoder;
    private readonly InferenceSession _embedTokens;
    private readonly InferenceSession _encoderModel;
    private readonly InferenceSession _decoderMerged;
    private readonly FlorenceTokenizer _tokenizer;

    // Architecture constants (Florence-2-base fine-tuned for icon_caption)
    private const int ImageSize = 768;
    private const int HiddenDim = 768;
    private const int NumHeads = 12;
    private const int HeadDim = 64;
    private const int NumLayers = 6;
    private const int MaxNewTokens = 10;
    private const int EosTokenId = 2;
    private const int DecoderStartTokenId = 2;
    private const int CaptionTokenCount = 6;

    // Pre-computed prompt embeddings [CaptionTokenCount * HiddenDim]
    private readonly float[] _promptEmbeds;

    // ImageNet normalize (CLIP-style)
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    // KV-cache layout: [NumLayers][4 types (dec_key, dec_val, enc_key, enc_val)]
    private static readonly string[] KvSuffixes = ["decoder.key", "decoder.value", "encoder.key", "encoder.value"];

    // Index mapping: present output name → past input name, built once from model metadata
    private readonly Dictionary<string, string> _presentToPastName;
    private readonly string[] _decoderOutputNames;
    private readonly RunOptions _runOptions;

    public FlorenceCaptioner(string modelDir, SessionOptions? options = null)
    {
        options ??= CreateDefaultSessionOptions();

        _visionEncoder = new InferenceSession(Path.Combine(modelDir, "vision_encoder.onnx"), options);
        _embedTokens = new InferenceSession(Path.Combine(modelDir, "embed_tokens.onnx"), options);
        _encoderModel = new InferenceSession(Path.Combine(modelDir, "encoder_model.onnx"), options);
        _decoderMerged = new InferenceSession(Path.Combine(modelDir, "decoder_model_merged.onnx"), options);

        _tokenizer = new FlorenceTokenizer(Path.Combine(modelDir, "vocab.json"));

        _decoderOutputNames = _decoderMerged.OutputNames.ToArray();

        // Build present→past name mapping from model outputs
        _presentToPastName = new Dictionary<string, string>();
        var outputNames = _decoderMerged.OutputNames;
        foreach (var outName in outputNames)
        {
            if (outName.StartsWith("present."))
            {
                var pastName = outName.Replace("present.", "past_key_values.");
                _presentToPastName[outName] = pastName;
            }
        }

        _runOptions = new();
        // Pre-compute prompt token embeddings
        _promptEmbeds = ComputePromptEmbeds();

    }

    /// <summary>
    /// Generate caption for a single icon region.
    /// </summary>
    public string? CaptionIcon(Bitmap screenshot, Rectangle iconBox)
    {
        var captions = CaptionIcons(screenshot, [iconBox]);
        return captions[0];
    }

    /// <summary>
    /// Generate captions for multiple icon regions.
    /// Each icon is processed independently through the vision encoder,
    /// then through the shared text encoder + decoder.
    /// </summary>
    public string?[] CaptionIcons(Bitmap screenshot, IReadOnlyList<Rectangle> iconBoxes)
    {
        if (iconBoxes.Count == 0) return [];

        var results = new string?[iconBoxes.Count];

        for (int i = 0; i < iconBoxes.Count; i++)
        {
            var pixelValues = PreprocessImage(screenshot, iconBoxes[i]);
            var imageFeatures = RunVisionEncoder(pixelValues);

            int imgSeqLen = imageFeatures.Length / HiddenDim;
            int encoderSeqLen = imgSeqLen + CaptionTokenCount;

            var combinedEmbeds = new float[encoderSeqLen * HiddenDim];
            Buffer.BlockCopy(imageFeatures, 0, combinedEmbeds, 0, imageFeatures.Length * sizeof(float));
            Buffer.BlockCopy(_promptEmbeds, 0, combinedEmbeds, imageFeatures.Length * sizeof(float), _promptEmbeds.Length * sizeof(float));

            var encoderHidden = RunEncoder(combinedEmbeds, encoderSeqLen);

            results[i] = AutoregressiveDecode(encoderHidden, encoderSeqLen);
            Console.WriteLine($"{iconBoxes[i]} -> {results[i]}");
        }

        return results;
    }

    public void Dispose()
    {
        _runOptions.Dispose();
        _visionEncoder?.Dispose();
        _embedTokens?.Dispose();
        _encoderModel?.Dispose();
        _decoderMerged?.Dispose();
    }

    private float[] ComputePromptEmbeds()
    {
        var ids = FlorenceTokenizer.CaptionPromptIds;
        var inputIds = new long[ids.Length];
        for (int i = 0; i < ids.Length; i++) inputIds[i] = ids[i];

        var inputShape = new long[] { 1, ids.Length };
        using var inputOrt = OrtValue.CreateTensorValueFromMemory(inputIds, inputShape);

        using var output = _embedTokens.Run(_runOptions, _embedTokens.InputNames, [inputOrt], _embedTokens.OutputNames);

        var embeds = new float[ids.Length * HiddenDim];
        output[0].GetTensorDataAsSpan<float>().CopyTo(embeds);
        return embeds;
    }

    private float[] PreprocessImage(Bitmap screenshot, Rectangle box)
    {
        var cropRect = Rectangle.Intersect(box, new Rectangle(0, 0, screenshot.Width, screenshot.Height));
        if (cropRect.Width <= 0 || cropRect.Height <= 0)
            return new float[3 * ImageSize * ImageSize];

        using var cropped = new Bitmap(cropRect.Width, cropRect.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(cropped))
        {
            g.DrawImage(screenshot, 0, 0, cropRect, GraphicsUnit.Pixel);
        }

        using var resized = new Bitmap(ImageSize, ImageSize, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.DrawImage(cropped, 0, 0, ImageSize, ImageSize);
        }

        var result = new float[3 * ImageSize * ImageSize];
        var bmpData = resized.LockBits(new Rectangle(0, 0, ImageSize, ImageSize),
            ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            var stride = bmpData.Stride;
            var rowBytes = new byte[stride];

            for (int y = 0; y < ImageSize; y++)
            {
                Marshal.Copy(bmpData.Scan0 + y * stride, rowBytes, 0, stride);
                for (int x = 0; x < ImageSize; x++)
                {
                    var offset = x * 3;
                    float b = rowBytes[offset + 0] / 255f;
                    float g = rowBytes[offset + 1] / 255f;
                    float r = rowBytes[offset + 2] / 255f;

                    result[0 * ImageSize * ImageSize + y * ImageSize + x] = (r - Mean[0]) / Std[0];
                    result[1 * ImageSize * ImageSize + y * ImageSize + x] = (g - Mean[1]) / Std[1];
                    result[2 * ImageSize * ImageSize + y * ImageSize + x] = (b - Mean[2]) / Std[2];
                }
            }
        }
        finally
        {
            resized.UnlockBits(bmpData);
        }

        return result;
    }

    private float[] RunVisionEncoder(float[] pixelValues)
    {
        var inputShape = new long[] { 1, 3, ImageSize, ImageSize };
        using var inputOrt = OrtValue.CreateTensorValueFromMemory(pixelValues, inputShape);

        using var output = _visionEncoder.Run(_runOptions, 
            _visionEncoder.InputNames, [inputOrt],
            _visionEncoder.OutputNames);

        var features = new float[output[0].GetTensorTypeAndShape().Shape[1] * HiddenDim];
        output[0].GetTensorDataAsSpan<float>().CopyTo(features);
        return features;
    }

    private float[] RunEncoder(float[] combinedEmbeds, int seqLen)
    {
        var embedsShape = new long[] { 1, seqLen, HiddenDim };
        var maskShape = new long[] { 1, seqLen };
        var attnMask = new long[seqLen];
        Array.Fill(attnMask, 1L);

        using var embedsOrt = OrtValue.CreateTensorValueFromMemory(combinedEmbeds, embedsShape);
        using var maskOrt = OrtValue.CreateTensorValueFromMemory(attnMask, maskShape);

        using var output = _encoderModel.Run(_runOptions,
            _encoderModel.InputNames,
            [embedsOrt, maskOrt],
            _encoderModel.OutputNames);

        var hidden = new float[seqLen * HiddenDim];
        output[0].GetTensorDataAsSpan<float>().CopyTo(hidden);
        return hidden;
    }

    private string? AutoregressiveDecode(float[] encoderHidden, int encoderSeqLen)
    {
        // KV-cache storage: [layer][kv_type] → float array
        // kv_type: 0=dec_key, 1=dec_val, 2=enc_key, 3=enc_val
        float[][]?[] kvCache = new float[][]?[NumLayers];

        // Embed the decoder start token
        var startEmbeds = EmbedToken(DecoderStartTokenId);

        // Encoder hidden states — shared across all decoder steps
        var encHiddenShape = new long[] { 1, encoderSeqLen, HiddenDim };

        var generatedIds = new List<int>(MaxNewTokens);
        int nextTokenId;

        // === First step: use_cache_branch = false, zero past KV ===
        {
            var inputs = new List<OrtValue>();
            var disposables = new List<IDisposable>();

            try
            {
                // encoder_hidden_states
                var encHiddenOrt = OrtValue.CreateTensorValueFromMemory(encoderHidden, encHiddenShape);
                inputs.Add(encHiddenOrt);
                disposables.Add(encHiddenOrt);

                // inputs_embeds [1, 1, 768]
                var embedsOrt = OrtValue.CreateTensorValueFromMemory(startEmbeds, [1, 1, HiddenDim]);
                inputs.Add(embedsOrt);
                disposables.Add(embedsOrt);

                // past KV: all zeros, decoder past has length 0, encoder past has length encoderSeqLen
                var emptyDecoderShape = new long[] { 1, NumHeads, 0, HeadDim };
                var encKvShape = new long[] { 1, NumHeads, encoderSeqLen, HeadDim };
                for (int l = 0; l < NumLayers; l++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        if (k < 2) // decoder key/value — empty
                        {
                            var ortVal = OrtValue.CreateTensorValueFromMemory(Array.Empty<float>(), emptyDecoderShape);
                            inputs.Add(ortVal);
                            disposables.Add(ortVal);
                        }
                        else // encoder key/value — zeros
                        {
                            var data = new float[NumHeads * encoderSeqLen * HeadDim];
                            var ortVal = OrtValue.CreateTensorValueFromMemory(data, encKvShape);
                            inputs.Add(ortVal);
                            disposables.Add(ortVal);
                        }
                    }
                }

                // use_cache_branch = false
                var useCacheOrt = OrtValue.CreateTensorValueFromMemory(new bool[] { false }, [1]);
                inputs.Add(useCacheOrt);
                disposables.Add(useCacheOrt);

               using var output = _decoderMerged.Run(_runOptions,_decoderMerged.InputNames, inputs, _decoderOutputNames);

                nextTokenId = ArgMax(output[0].GetTensorDataAsSpan<float>());
                generatedIds.Add(nextTokenId);

                // Extract present KV into float arrays (copy data, then dispose OrtValues)
                ExtractKvCache(output, kvCache, 0);
                //Console.WriteLine($"After step0 extract: dec_key={kvCache[0]?[0]?.Length}, dec_val={kvCache[0]?[1]?.Length}, enc_key={kvCache[0]?[2]?.Length}, enc_val={kvCache[0]?[3]?.Length}");
            }
            finally
            {
                foreach (var d in disposables) d?.Dispose();
            }
        }

        // === Subsequent steps: use_cache_branch = true, with past KV ===
        for (int step = 1; step < MaxNewTokens; step++)
        {
            if (nextTokenId == EosTokenId) break;

            var stepEmbeds = EmbedToken(nextTokenId);

            var inputs = new List<OrtValue>();
            var disposables = new List<IDisposable>();

            try
            {
                // encoder_hidden_states
                var encHiddenOrt = OrtValue.CreateTensorValueFromMemory(encoderHidden, encHiddenShape);
                inputs.Add(encHiddenOrt);
                disposables.Add(encHiddenOrt);

                // inputs_embeds [1, 1, 768]
                var embedsOrt = OrtValue.CreateTensorValueFromMemory(stepEmbeds, [1, 1, HiddenDim]);
                inputs.Add(embedsOrt);
                disposables.Add(embedsOrt);

                // past KV from cache
                int pastDecoderLen = step;  // number of past decoder tokens
                for (int l = 0; l < NumLayers; l++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        long[] shape;
                        if (k < 2) // decoder key/value
                            shape = [1, NumHeads, pastDecoderLen, HeadDim];
                        else // encoder key/value
                            shape = [1, NumHeads, encoderSeqLen, HeadDim];

                        var data = kvCache[l]?[k];
                        if (data == null || data.Length == 0)
                        {
                            data = new float[(int)(shape[1] * shape[2] * shape[3])];
                        }

                        var ortVal = OrtValue.CreateTensorValueFromMemory(data, shape);
                        inputs.Add(ortVal);
                        disposables.Add(ortVal);
                    }
                }

                // use_cache_branch = true
                var useCacheOrt = OrtValue.CreateTensorValueFromMemory(new bool[] { true }, [1]);
                inputs.Add(useCacheOrt);
                disposables.Add(useCacheOrt);

                using var output = _decoderMerged.Run(_runOptions,_decoderMerged.InputNames, inputs, _decoderOutputNames);

                nextTokenId = ArgMax(output[0].GetTensorDataAsSpan<float>());
                generatedIds.Add(nextTokenId);

                ExtractKvCache(output, kvCache, step);
            }
            finally
            {
                foreach (var d in disposables) d?.Dispose();
            }
        }

        // Remove trailing EOS
        if (generatedIds.Count > 0 && generatedIds.Last() == EosTokenId)
            generatedIds.RemoveAt(generatedIds.Count - 1);

        return generatedIds.Count > 0
            ? _tokenizer.Decode(generatedIds.ToArray())
            : null;
    }

    /// <summary>
    /// Embed a single token ID using the embed_tokens model.
    /// </summary>
    private float[] EmbedToken(int tokenId)
    {
        var ids = new long[] { tokenId };
        using var inputOrt = OrtValue.CreateTensorValueFromMemory(ids, [1, 1]);

        using var output = _embedTokens.Run(_runOptions,_embedTokens.InputNames, [inputOrt], _embedTokens.OutputNames);

        var embeds = new float[HiddenDim];
        output[0].GetTensorDataAsSpan<float>().CopyTo(embeds);
        return embeds;
    }

    /// <summary>
    /// Extract KV-cache from decoder output into float arrays.
    /// Data is copied from OrtValues so they can be safely disposed.
    /// </summary>
    private void ExtractKvCache(IReadOnlyList<OrtValue> output, float[][]?[] kvCache, int currentStep)
    {
        //if (currentStep == 0)
        //    Console.WriteLine("Decoder output names: " + string.Join(", ", _decoderOutputNames));
        for (int l = 0; l < NumLayers; l++)
        {
            kvCache[l] ??= new float[4][];

            for (int k = 0; k < 4; k++)
            {
                var presentName = $"present.{l}.{KvSuffixes[k]}";

                // Find the output index for this present name
                int outIdx = -1;
                for (int j = 1; j < _decoderOutputNames.Length; j++)
                {
                    if (_decoderOutputNames[j] == presentName)
                    {
                        outIdx = j;
                        break;
                    }
                }

                if (outIdx >= 0 && outIdx < output.Count)
                {
                    var shape = output[outIdx].GetTensorTypeAndShape().Shape;
                    //if (currentStep == 0 && l == 0)
                    //    Console.WriteLine($"  present.{l}.{KvSuffixes[k]}: [{string.Join(",", shape)}] span={output[outIdx].GetTensorDataAsSpan<float>().Length}");
                    var data = output[outIdx].GetTensorDataAsSpan<float>();
                    kvCache[l]![k] = data.ToArray();
                }
                else if (currentStep == 0 && l == 0)
                {
                    //Console.WriteLine($"ExtractKvCache: outIdx={outIdx}, output.Count={output.Count}, presentName={presentName}");
                }
            }
        }
    }

    private static int ArgMax(ReadOnlySpan<float> values)
    {
        int maxIdx = 0;
        float maxVal = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > maxVal)
            {
                maxVal = values[i];
                maxIdx = i;
            }
        }
        return maxIdx;
    }

    private static SessionOptions CreateDefaultSessionOptions()
    {
        var options = new SessionOptions();
        options.AppendExecutionProvider_DML();
        options.AppendExecutionProvider_CPU();
        return options;
    }
}
