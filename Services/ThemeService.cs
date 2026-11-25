using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Styling;

namespace CityHelper.Services;

/// <summary>The color theme of the whole application: dark, light, or whatever the operating system uses.</summary>
public static class ThemeService
{
    public const string SystemChoice = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";

    public static IReadOnlyList<string> Choices { get; } = new[] { SystemChoice, Light, Dark };

    /// <summary>Normalises a saved value; anything unknown means Dark (how the app has always looked).</summary>
    public static string Normalize(string? value)
    {
        foreach (var choice in Choices)
            if (choice.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase))
                return choice;
        return Dark;
    }

    /// <summary>Applies the theme to every window and section at once.</summary>
    public static void Apply(string? choice)
    {
        if (Application.Current is not { } app)
            return;
        app.RequestedThemeVariant = Normalize(choice) switch
        {
            Light => ThemeVariant.Light,
            Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,      // follow the system
        };
    }
}
