using System;
using System.Text;

namespace CityHelper.Services;

/// <summary>Application-wide settings, in the [App] section of CityHelper.ini.</summary>
public sealed class AppSettings
{
    /// <summary>True once first-run setup has been completed or skipped.</summary>
    public bool SetupCompleted { get; set; }

    /// <summary>The person answered No to loading the macro images at start; they are not asked again.</summary>
    public bool MacroImagePromptDismissed { get; set; }

    /// <summary>Size of the main window when it was last closed; 0 means not saved yet (use the default size).</summary>
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }

    /// <summary>The main window was maximized when it was last closed.</summary>
    public bool WindowMaximized { get; set; }

    /// <summary>Color theme: System, Light or Dark.</summary>
    public string Theme { get; set; } = ThemeService.Dark;

    public static AppSettings Load()
    {
        var s = new AppSettings();
        var lines = IniStore.ReadLines("App", "");
        if (lines != null)
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.Length == 0 || line[0] is '#' or ';' or '[' || eq < 0)
                    continue;
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                if (key.Equals("setup_completed", StringComparison.OrdinalIgnoreCase))
                    s.SetupCompleted = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                else if (key.Equals("macro_image_prompt_dismissed", StringComparison.OrdinalIgnoreCase))
                    s.MacroImagePromptDismissed = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                else if (key.Equals("window_width", StringComparison.OrdinalIgnoreCase))
                    s.WindowWidth = double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w) && w > 0 ? w : 0;
                else if (key.Equals("window_height", StringComparison.OrdinalIgnoreCase))
                    s.WindowHeight = double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) && h > 0 ? h : 0;
                else if (key.Equals("window_maximized", StringComparison.OrdinalIgnoreCase))
                    s.WindowMaximized = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                else if (key.Equals("theme", StringComparison.OrdinalIgnoreCase))
                    s.Theme = ThemeService.Normalize(value);
            }

        // Someone who already has installations saved (from before setup existed) doesn't need it
        if (!s.SetupCompleted && InstallationStore.Load() is { Count: > 0 })
            s.SetupCompleted = true;
        return s;
    }

    public void Save()
    {
        var sb = new StringBuilder();
        sb.Append("[Settings]\n");
        sb.Append("setup_completed = ").Append(SetupCompleted ? "true" : "false").Append('\n');
        sb.Append("macro_image_prompt_dismissed = ").Append(MacroImagePromptDismissed ? "true" : "false").Append('\n');
        sb.Append("theme = ").Append(ThemeService.Normalize(Theme)).Append('\n');
        if (WindowWidth > 0 && WindowHeight > 0)
        {
            sb.Append("window_width = ").Append(Math.Round(WindowWidth).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("window_height = ").Append(Math.Round(WindowHeight).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }
        sb.Append("window_maximized = ").Append(WindowMaximized ? "true" : "false").Append('\n');
        IniStore.WriteText("App", sb.ToString());
    }
}
