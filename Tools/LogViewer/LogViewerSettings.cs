using System;
using System.IO;
using System.Text;

namespace CityHelper.Tools.LogViewer;

/// <summary>What to do with the color tags (e.g. &lt;color #ff0101&gt;) embedded in chat text. They are not drawn as colors for now.</summary>
public enum ColorCodeMode
{
    Hide = 0,       // strip the tags, plain text

    ShowRaw = 1,    // leave the tags in the text, as written in the log
}

/// <summary>Log Viewer options, stored in logviewer.ini in the application directory.</summary>
public sealed class LogViewerSettings
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "logviewer.ini");

    // Options window
    public ColorCodeMode ColorCodes { get; set; } = ColorCodeMode.Hide;
    public bool UseChannelColors { get; set; } = true;
    public bool WordWrap { get; set; } = true;

    // Toolbar toggles (which parts of each line are shown)
    public bool ShowDates { get; set; }
    public bool ShowTimes { get; set; } = true;
    public bool ShowChannels { get; set; } = true;
    public bool ShowCharacters { get; set; } = true;

    /// <summary>Mark the lines Log Manager's cleanup would remove.</summary>
    public bool ShowCleanup { get; set; }

    public static LogViewerSettings Load(string? path = null)
    {
        var s = new LogViewerSettings();
        path ??= DefaultPath;
        if (!File.Exists(path))
            return s;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] is '#' or ';' or '[' || eq < 0)
                continue;
            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            bool on = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            switch (key)
            {
                case "color_codes":
                    s.ColorCodes = Enum.TryParse<ColorCodeMode>(value, true, out var mode) && Enum.IsDefined(mode) ? mode : ColorCodeMode.Hide;   // an old "UseColor" becomes Hide
                    break;
                case "channel_colors": s.UseChannelColors = on; break;
                case "word_wrap": s.WordWrap = on; break;
                case "show_dates": s.ShowDates = on; break;
                case "show_times": s.ShowTimes = on; break;
                case "show_channels": s.ShowChannels = on; break;
                case "show_characters": s.ShowCharacters = on; break;
                case "show_cleanup": s.ShowCleanup = on; break;
            }
        }
        return s;
    }

    public void Save(string? path = null)
    {
        var sb = new StringBuilder();
        sb.Append("[Options]\n");
        sb.Append("color_codes = ").Append(ColorCodes).Append('\n');
        sb.Append("channel_colors = ").Append(B(UseChannelColors)).Append('\n');
        sb.Append("word_wrap = ").Append(B(WordWrap)).Append("\n\n");
        sb.Append("[Display]\n");
        sb.Append("show_dates = ").Append(B(ShowDates)).Append('\n');
        sb.Append("show_times = ").Append(B(ShowTimes)).Append('\n');
        sb.Append("show_channels = ").Append(B(ShowChannels)).Append('\n');
        sb.Append("show_characters = ").Append(B(ShowCharacters)).Append('\n');
        sb.Append("show_cleanup = ").Append(B(ShowCleanup)).Append('\n');
        File.WriteAllText(path ?? DefaultPath, sb.ToString());
    }

    private static string B(bool b) => b ? "true" : "false";
}
