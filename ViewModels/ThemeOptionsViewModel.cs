using System;
using System.Collections.Generic;
using System.IO;
using CityHelper.Services;
using CityHelper.Services.Emotes;
using CityHelper.Services.MacroImages;
using CityHelper.Services.SlashCommands;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CityHelper.ViewModels;

/// <summary>The "System" options page: the color theme (light, dark, or the system's).</summary>
public sealed partial class ThemeOptionsViewModel : OptionsSectionViewModel
{
    public ThemeOptionsViewModel()
    {
        _selectedTheme = AppSettings.Load().Theme;
        _macroImageStatus = MacroImageCache.Describe();
        _slashCommandStatus = SlashCommandCache.Describe();
        _emoteStatus = EmoteCache.Describe();
    }

    public override string Title => "System";

    public IReadOnlyList<string> Themes => ThemeService.Choices;

    [ObservableProperty]
    private string _selectedTheme;

    /// <summary>What is cached (shown under the macro images button).</summary>
    [ObservableProperty]
    private string _macroImageStatus;

    public void RefreshMacroImageStatus() => MacroImageStatus = MacroImageCache.Describe();

    /// <summary>How many slash commands are loaded and where they came from.</summary>
    [ObservableProperty]
    private string _slashCommandStatus;

    public void RefreshSlashCommandStatus() => SlashCommandStatus = SlashCommandCache.Describe();

    /// <summary>How many emotes are loaded and where they came from.</summary>
    [ObservableProperty]
    private string _emoteStatus;

    public void RefreshEmoteStatus() => EmoteStatus = EmoteCache.Describe();

    /// <summary>How many badges are loaded and where they came from.</summary>
    [ObservableProperty]
    private string _badgeStatus = Services.Badges.BadgeCache.Describe();

    public async System.Threading.Tasks.Task UpdateBadgesAsync()
    {
        BadgeStatus = "Downloading...";
        var result = await Services.Badges.BadgeCache.DownloadAsync();
        BadgeStatus = result.Error is null ? Services.Badges.BadgeCache.Describe() : "Could not update: " + result.Error;
    }

    public override string? Save()
    {
        try
        {
            var settings = AppSettings.Load();
            settings.Theme = ThemeService.Normalize(SelectedTheme);
            settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Could not write CityHelper.ini:\n" + ex.Message;
        }
        ThemeService.Apply(SelectedTheme);       // every section changes at once
        return null;
    }
}
