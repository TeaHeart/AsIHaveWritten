namespace AsIHaveWritten.Helpers;

using Microsoft.VisualBasic;

public static class StringHelper
{
    public static string ToSimplifiedChinese(string text)
    {
        if (Strings.StrConv(text, VbStrConv.SimplifiedChinese) is string chText)
        {
            return chText;
        }
        return text;
    }

    public static bool IsSimplifiedChinese(char c)
    {
        // 基本汉字
        if (c >= '\u4E00' && c <= '\u9FFF')
        {
            return true;
        }

        // CJK扩展A区
        if (c >= '\u3400' && c <= '\u4DBF')
        {
            return true;
        }

        return false;
    }

    public static string FindBestMatch(IEnumerable<string> dict, string text, float threshold = 0.5f)
    {
        var bestMatch = text;
        var minDis = text.Length;

        foreach (var item in dict)
        {
            var dis = LevenshteinDistance(item, text);
            if (dis < minDis)
            {
                minDis = dis;
                bestMatch = item;
            }
        }

        var maxLength = Math.Max(bestMatch.Length, text.Length);
        var score = 1.0f - (float)minDis / maxLength;
        return score >= threshold ? bestMatch : text;
    }

    public static int LevenshteinDistance(string s, string t)
    {
        var n = s.Length;
        var m = t.Length;

        if (n == 0)
        {
            return m;
        }

        if (m == 0)
        {
            return n;
        }

        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= m; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                var cost = t[j - 1] == s[i - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
