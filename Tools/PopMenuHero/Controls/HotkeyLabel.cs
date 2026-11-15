using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using CityHelper.Tools.PopMenuHero.Core;

namespace CityHelper.Tools.PopMenuHero.Controls;

/// <summary>TextBlock that renders &amp;X hotkey markers as a bold letter.</summary>
public class HotkeyLabel : TextBlock
{
    public static readonly StyledProperty<string?> MarkupProperty =
        AvaloniaProperty.Register<HotkeyLabel, string?>(nameof(Markup));

    static HotkeyLabel()
    {
        MarkupProperty.Changed.AddClassHandler<HotkeyLabel>((label, _) => label.Rebuild());
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Markup
    {
        get => GetValue(MarkupProperty);
        set => SetValue(MarkupProperty, value);
    }

    private void Rebuild()
    {
        var inlines = new InlineCollection();
        foreach (var segment in HotkeyText.Split(Markup ?? ""))
        {
            inlines.Add(segment.IsHotkey
                ? new Run(segment.Text) { FontWeight = FontWeight.Bold }
                : new Run(segment.Text));
        }
        Inlines = inlines;
    }
}
