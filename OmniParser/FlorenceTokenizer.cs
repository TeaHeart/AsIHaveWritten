namespace OmniParser;

using System.Text;
using System.Text.Json;

/// <summary>
/// Lightweight tokenizer for Florence-2 caption decoding.
/// Encodes the fixed &lt;CAPTION&gt; prompt; decodes generated token IDs to text.
/// </summary>
internal sealed class FlorenceTokenizer
{
    // Fixed prompt token IDs for "<CAPTION>" = <s> + split("<CAPTION>") + </s>
    public static readonly int[] CaptionPromptIds = [0, 41552, 28494, 10263, 15698, 2];

    private const int EosTokenId = 2;
    private const int PadTokenId = 1;

    // id → token string (for decoding)
    private readonly string[] _idToToken;

    public FlorenceTokenizer(string vocabPath)
    {
        var json = File.ReadAllText(vocabPath);
        var vocabMap = JsonSerializer.Deserialize<Dictionary<string, int>>(json)
            ?? throw new InvalidDataException("Failed to parse vocab.json");

        var maxId = vocabMap.Values.Max();
        _idToToken = new string[maxId + 1];
        foreach (var (token, id) in vocabMap)
        {
            _idToToken[id] = token;
        }
    }

    /// <summary>
    /// Decode generated token IDs to plain text.
    /// Handles GPT-2 BPE byte-fallback tokens (Ġ=space, â/ĺ/ģ/Ķ etc.).
    /// </summary>
    public string Decode(ReadOnlySpan<int> ids)
    {
        var sb = new StringBuilder(ids.Length * 4);

        foreach (var id in ids)
        {
            if (id == EosTokenId || id == PadTokenId || id == 0)
                continue;

            if (id < 0 || id >= _idToToken.Length || _idToToken[id] is null)
                continue;

            sb.Append(_idToToken[id]);
        }

        // Convert GPT-2 BPE bytes back to UTF-8 string
        return BpeBytesToText(sb.ToString()).Trim();
    }

    /// <summary>
    /// Convert GPT-2 style BPE token bytes to readable text.
    /// GPT-2 uses a byte-level BPE where bytes are mapped to printable Unicode:
    /// e.g. Ġ = space (0x20), âĺĠ = bytes that decode to multi-byte UTF-8 chars.
    /// </summary>
    private static string BpeBytesToText(string bpeText)
    {
        // Map from GPT-2 BPE unicode chars back to raw bytes
        var bytes = new List<byte>(bpeText.Length);
        foreach (var ch in bpeText)
        {
            var offset = ByteEncoder.IndexOf(ch);
            if (offset >= 0)
            {
                bytes.Add((byte)offset);
            }
            else
            {
                // Fallback: encode char as UTF-8 bytes directly
                var encoded = Encoding.UTF8.GetBytes(ch.ToString());
                bytes.AddRange(encoded);
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    // GPT-2 byte-to-unicode mapping: byte value → unicode char
    // This is the standard mapping from openai/gpt-2 encoder.py
    private static readonly string ByteEncoder = BuildByteEncoder();

    private static string BuildByteEncoder()
    {
        var bs = new List<int>();
        // Printable ASCII range that maps 1:1
        for (int i = '!'; i <= '~'; i++) bs.Add(i);       // 33-126
        for (int i = '¡'; i <= '¬'; i++) bs.Add(i);       // 161-172
        for (int i = '®'; i <= 'ÿ'; i++) bs.Add(i);       // 174-255

        var cs = new List<int>(bs);
        int n = 0;
        for (int b = 0; b < 256; b++)
        {
            if (!bs.Contains(b))
            {
                bs.Add(b);
                cs.Add(256 + n);
                n++;
            }
        }

        // Build char array indexed by byte value
        var chars = new char[256];
        for (int i = 0; i < 256; i++)
        {
            var byteVal = bs[i];
            chars[byteVal] = (char)cs[i];
        }

        return new string(chars);
    }
}
