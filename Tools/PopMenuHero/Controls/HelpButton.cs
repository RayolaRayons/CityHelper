using System;
using Avalonia;
using Avalonia.Controls;

namespace CityHelper.Tools.PopMenuHero.Controls;

/// <summary>Small "?" button that opens a reference web page.</summary>
public class HelpButton : Button
{
    public static readonly StyledProperty<string?> UrlProperty =
        AvaloniaProperty.Register<HelpButton, string?>(nameof(Url));

    public HelpButton()
    {
        Content = "?";
        Width = 28;
        Focusable = false;
    }

    protected override Type StyleKeyOverride => typeof(Button);

    public string? Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (Url != null && TopLevel.GetTopLevel(this) is { } top)
            _ = top.Launcher.LaunchUriAsync(new Uri(Url));
    }
}
