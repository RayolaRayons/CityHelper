namespace CityHelper.Tools.PopMenuHero.Core;

internal static class MnuTokenizer
{
    /// <summary>Remove quotes or &lt;&amp; &amp;&gt; delimiters from a token.</summary>
    public static string Unquote(string token)
    {
        token = token.Trim();
        if (token.StartsWith("<&", StringComparison.Ordinal) && token.EndsWith("&>", StringComparison.Ordinal))
            return token.Length >= 4 ? token[2..^2].Trim() : "";
        if (token.StartsWith('"') && token.EndsWith('"'))
            return token.Length >= 2 ? token[1..^1] : "";
        return token;
    }

    /// <summary>
    /// Tokenize a single line, handling quoted strings and &lt;&amp; &amp;&gt; blocks.
    /// Returns list of raw tokens (still quoted).
    /// </summary>
    public static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        line = line.Trim();
        int i = 0;
        while (i < line.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(line[i]))
            {
                i++;
                continue;
            }
            // Comment - rest of line is one token
            if (StartsAt(line, i, "//"))
            {
                tokens.Add(line[i..]);
                break;
            }
            // <& ... &> token
            if (StartsAt(line, i, "<&"))
            {
                int end = line.IndexOf("&>", i + 2, StringComparison.Ordinal);
                if (end == -1)
                {
                    tokens.Add(line[i..]);
                    break;
                }
                tokens.Add(line[i..(end + 2)]);
                i = end + 2;
                continue;
            }
            // Quoted string
            if (line[i] == '"')
            {
                int j = i + 1;
                while (j < line.Length && line[j] != '"')
                {
                    if (line[j] == '\\')
                        j++;  // skip escaped char
                    j++;
                }
                tokens.Add(line[i..Math.Min(j + 1, line.Length)]);
                i = j + 1;
                continue;
            }
            // Bare word (keyword, DIVIDER, {, })
            int k = i;
            while (k < line.Length && !char.IsWhiteSpace(line[k]) && line[k] != '"')
            {
                if (StartsAt(line, k, "<&"))
                    break;
                k++;
            }
            tokens.Add(line[i..k]);
            i = k;
        }
        return tokens;
    }

    private static bool StartsAt(string s, int index, string value) =>
        string.CompareOrdinal(s, index, value, 0, value.Length) == 0;
}
