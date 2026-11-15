using System.Text;

namespace CityHelper.Tools.PopMenuHero.Core;

/// <summary>Splits display text on &amp;X hotkey markers so the hotkey letter can be shown bold.</summary>
public static class HotkeyText
{
    public readonly record struct Segment(string Text, bool IsHotkey);

    /// <summary>
    /// '&amp;' followed by a character marks that character as the hotkey (the '&amp;' is dropped).
    /// '&amp;' followed by a space, or at the end of the text, is a literal ampersand.
    /// </summary>
    public static List<Segment> Split(string text)
    {
        var result = new List<Segment>();
        var plain = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '&' && i + 1 < text.Length && text[i + 1] != ' ')
            {
                int len = char.IsHighSurrogate(text[i + 1]) && i + 2 < text.Length ? 2 : 1;
                if (plain.Length > 0)
                {
                    result.Add(new Segment(plain.ToString(), false));
                    plain.Clear();
                }
                result.Add(new Segment(text.Substring(i + 1, len), true));
                i += 1 + len;
            }
            else
            {
                plain.Append(text[i]);
                i++;
            }
        }
        if (plain.Length > 0)
            result.Add(new Segment(plain.ToString(), false));
        return result;
    }
}
