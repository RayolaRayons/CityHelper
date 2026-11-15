using System;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using CityHelper.Tools.LogViewer.ViewModels;

namespace CityHelper.Tools.LogViewer.Views;

/// <summary>
/// Draws one log line: date, time, [channel], character and the message, honouring the display options
/// (which parts to show, how to treat color tags, channel colors, word wrap). Built when a row is realized.
/// </summary>
public class LogLineText : TextBlock
{
    public static readonly StyledProperty<LogLine?> LineProperty =
        AvaloniaProperty.Register<LogLineText, LogLine?>(nameof(Line));

    public static readonly StyledProperty<DisplayOptions?> OptionsProperty =
        AvaloniaProperty.Register<LogLineText, DisplayOptions?>(nameof(Options));

    private static readonly IBrush Dim = new SolidColorBrush(Color.Parse("#858585"));
    private static readonly IBrush CharacterBlue = new SolidColorBrush(Color.Parse("#7FB2FF"));
    private static readonly IBrush HighlightBackground = new SolidColorBrush(Color.Parse("#E8C547"));
    private static readonly IBrush HighlightForeground = new SolidColorBrush(Color.Parse("#101010"));
    private static readonly IBrush RemovedBackground = new SolidColorBrush(Color.Parse("#99B02020"));
    private static readonly IBrush ChannelGreen = new SolidColorBrush(Color.Parse("#6EBF6E"));

    static LogLineText()
    {
        LineProperty.Changed.AddClassHandler<LogLineText>((c, _) => c.Rebuild());
        OptionsProperty.Changed.AddClassHandler<LogLineText>((c, _) => c.Rebuild());
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public LogLine? Line
    {
        get => GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    public DisplayOptions? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    private void Rebuild()
    {
        if (Line is not { } line || Options is not { } o)
        {
            Inlines = new InlineCollection();
            return;
        }

        TextWrapping = o.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Background = o.IsRemovedByCleanup(line) ? RemovedBackground : null;     // the whole line, wrapped or not
        var inlines = new InlineCollection();

        IBrush? channelBrush = o.ChannelColors ? new SolidColorBrush(ChannelColors.For(line.ChannelKey)) : null;

        if (o.ShowDates && line.Date.Length > 0)
            inlines.Add(new Run(line.Date + " ") { Foreground = Dim });
        if (o.ShowTimes && line.Time.Length > 0)
            inlines.Add(new Run(line.Time + " ") { Foreground = Dim });
        if (o.ShowChannels && line.Channel.Length > 0)
            inlines.Add(new Run($"[{line.Channel}] ") { Foreground = channelBrush ?? ChannelGreen });
        if (o.ShowCharacters && line.ShowCharacterPrefix && line.Character.Length > 0)
            AddText(inlines, line.Character + ": ", channelBrush ?? CharacterBlue, null, FontWeight.SemiBold, o.Highlight);

        // The game's color tags are not drawn (they did not match how City of Heroes uses them): hidden, or shown as written
        AddText(inlines, LogLineFormatter.MessageText(line, o.ColorCodes), channelBrush, null, FontWeight.Normal, o.Highlight);
        Inlines = inlines;
    }

    /// <summary>Adds the text as runs, with the parts matching the highlight drawn on a highlight background.</summary>
    private static void AddText(InlineCollection inlines, string text, IBrush? foreground, IBrush? background, FontWeight weight, Regex? highlight)
    {
        int position = 0;
        if (highlight != null)
        {
            foreach (Match m in highlight.Matches(text))
            {
                if (m.Length == 0) continue;
                if (m.Index > position)
                    inlines.Add(Run(text[position..m.Index], foreground, background, weight));
                inlines.Add(Run(m.Value, HighlightForeground, HighlightBackground, weight));
                position = m.Index + m.Length;
            }
        }
        if (position < text.Length)
            inlines.Add(Run(text[position..], foreground, background, weight));
    }

    private static Run Run(string text, IBrush? foreground, IBrush? background, FontWeight weight)
    {
        var run = new Run(text) { FontWeight = weight };
        if (foreground != null) run.Foreground = foreground;
        if (background != null) run.Background = background;
        return run;
    }

}
