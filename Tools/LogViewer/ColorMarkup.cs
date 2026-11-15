using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia.Media;

namespace CityHelper.Tools.LogViewer;

/// <summary>A run of message text with the colors in effect for it (null = none set).</summary>
public readonly record struct TextSegment(string Text, Color? Foreground, Color? Background);

/// <summary>Reads the markup the game embeds in chat: &lt;color #ff0101&gt;, &lt;bgcolor yellow&gt;, &lt;scale 1.5&gt;, &lt;/color&gt;...</summary>
public static class ColorMarkup
{
    private static readonly Regex TagRe = new(@"<(?<close>/?)\s*(?<name>[A-Za-z0-9]+)(?:\s+(?<value>[^<>]*?))?\s*>", RegexOptions.Compiled);
    private static readonly Regex AnyTagRe = new(@"<[^<>\r\n]*>", RegexOptions.Compiled);

    /// <summary>Plain text with every &lt;...&gt; tag removed.</summary>
    public static string Strip(string text) => AnyTagRe.Replace(text, "");

    /// <summary>
    /// Splits text at the tags. With <paramref name="applyColors"/> the color / bgcolor tags set the colors of the
    /// segments that follow (until the matching closing tag); all other tags are dropped either way.
    /// </summary>
    public static List<TextSegment> Segments(string text, bool applyColors)
    {
        var result = new List<TextSegment>();
        var foregrounds = new Stack<Color?>();
        var backgrounds = new Stack<Color?>();
        Color? fg = null, bg = null;
        int position = 0;

        foreach (Match m in TagRe.Matches(text))
        {
            if (m.Index > position)
                result.Add(new TextSegment(text[position..m.Index], fg, bg));
            position = m.Index + m.Length;

            if (!applyColors)
                continue;

            var name = m.Groups["name"].Value.ToLowerInvariant();
            bool closing = m.Groups["close"].Value.Length > 0;
            if (name == "color")
                Apply(ref fg, foregrounds, closing, m.Groups["value"].Value);
            else if (name == "bgcolor")
                Apply(ref bg, backgrounds, closing, m.Groups["value"].Value);
        }

        if (position < text.Length)
            result.Add(new TextSegment(text[position..], fg, bg));
        return result;
    }

    private static void Apply(ref Color? current, Stack<Color?> stack, bool closing, string value)
    {
        if (closing)
        {
            current = stack.Count > 0 ? stack.Pop() : null;
            return;
        }
        stack.Push(current);
        current = Color.TryParse(value.Trim(), out var color) ? color : current;
    }
}

/// <summary>Default colors for channels (used when "use default colors" is on), chosen for the dark theme.</summary>
public static class ChannelColors
{
    private static readonly Dictionary<string, Color> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Local"] = Color.Parse("#E8E8E8"),
        ["Broadcast"] = Color.Parse("#FFD866"),
        ["Team"] = Color.Parse("#7FB2FF"),
        ["Supergroup"] = Color.Parse("#6EE7A0"),
        ["League"] = Color.Parse("#FFA559"),
        ["Coalition"] = Color.Parse("#D49BFF"),
        ["Tell"] = Color.Parse("#FF8FE8"),
        ["Request"] = Color.Parse("#5EE3D8"),
        ["Help"] = Color.Parse("#B4B4B4"),
        ["Looking For Group"] = Color.Parse("#A8E86B"),
        ["NPC"] = Color.Parse("#C9B28A"),
        ["Pet"] = Color.Parse("#C7A6FF"),
        ["Combat"] = Color.Parse("#FF8A80"),
        ["Emote"] = Color.Parse("#FFB86B"),
        ["System"] = Color.Parse("#9AA0A6"),
        ["Caption"] = Color.Parse("#FFD0A0"),
        ["PPD"] = Color.Parse("#9AA0A6"),
        ["FDPC"] = Color.Parse("#9AA0A6"),
        ["Nemesis"] = Color.Parse("#9AA0A6"),
        ["Vanguard"] = Color.Parse("#9AA0A6"),
    };

    // General, supergroup-named and player channels
    private static readonly Color Custom = Color.Parse("#8AB4F8");

    public static Color For(string channelKey) => Known.TryGetValue(channelKey, out var c) ? c : Custom;
}
