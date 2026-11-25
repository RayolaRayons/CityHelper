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
        ["Local"] = Color.Parse("#FFFFFF"),
        ["General"] = Color.Parse("#F4F4A0"),
        ["Broadcast"] = Color.Parse("#D8D8D8"),
        ["Team"] = Color.Parse("#4CD84C"),
        ["Supergroup"] = Color.Parse("#33C8F0"),
        ["League"] = Color.Parse("#8FA8F0"),
        ["Coalition"] = Color.Parse("#FFD060"),
        ["Tell"] = Color.Parse("#FFE81F"),
        ["Request"] = Color.Parse("#C47CF0"),
        ["Help"] = Color.Parse("#8FA8F0"),
        ["Looking For Group"] = Color.Parse("#30B8C0"),
        ["NPC"] = Color.Parse("#8080F0"),
        ["Pet"] = Color.Parse("#C8C8E0"),
        ["Combat"] = Color.Parse("#FF8C28"),
        ["Emote"] = Color.Parse("#FFFFFF"),
        ["System"] = Color.Parse("#3CC83C"),
        ["Reward"] = Color.Parse("#F4F060"),
        ["Auction"] = Color.Parse("#C47CF0"),
        ["Caption"] = Color.Parse("#7C7CF0"),
        ["PPD"] = Color.Parse("#B884F0"),
        ["FDPC"] = Color.Parse("#D8305A"),
        ["Nemesis"] = Color.Parse("#D8305A"),
        ["Council"] = Color.Parse("#B884F0"),
        ["Rikti"] = Color.Parse("#B884F0"),
        ["Vanguard"] = Color.Parse("#B884F0"),
    };

    // Player-made channels (Afterhours, GEMINI, CU Chat, Everlasting TFs...), pale aqua like in the game
    private static readonly Color Custom = Color.Parse("#7FF0D8");

    public static Color For(string channelKey) => Known.TryGetValue(channelKey, out var c) ? c : Custom;
}
