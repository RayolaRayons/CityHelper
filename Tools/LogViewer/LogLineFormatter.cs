using System.Collections.Generic;
using System.Linq;
using System.Text;
using CityHelper.Tools.LogViewer.ViewModels;

namespace CityHelper.Tools.LogViewer;

/// <summary>Turns log lines into plain text the way the viewer shows them (hidden parts and color tags are left out).</summary>
public static class LogLineFormatter
{
    public static string Format(LogLine line, DisplayOptions o)
    {
        var sb = new StringBuilder();
        if (o.ShowDates && line.Date.Length > 0)
            sb.Append(line.Date).Append(' ');
        if (o.ShowTimes && line.Time.Length > 0)
            sb.Append(line.Time).Append(' ');
        if (o.ShowChannels && line.Channel.Length > 0)
            sb.Append('[').Append(line.Channel).Append("] ");
        if (o.ShowCharacters && line.ShowCharacterPrefix && line.Character.Length > 0)
            sb.Append(line.Character).Append(": ");

        sb.Append(MessageText(line, o.ColorCodes));
        return sb.ToString();
    }

    /// <summary>The message as displayed: raw tags only in "show raw" mode, otherwise they are removed.</summary>
    public static string MessageText(LogLine line, ColorCodeMode mode) =>
        mode == ColorCodeMode.ShowRaw
            ? line.Message
            : string.Concat(ColorMarkup.Segments(line.Message, false).Select(s => s.Text));

    /// <summary>The lines in log order, one per line of text.</summary>
    public static string Format(IEnumerable<LogLine> lines, DisplayOptions o) =>
        string.Join("\r\n", lines.OrderBy(l => l.Number).Select(l => Format(l, o)));
}
