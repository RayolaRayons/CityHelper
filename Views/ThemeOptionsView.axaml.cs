using Avalonia.Controls;
using Avalonia.Interactivity;
using CityHelper.Services.Emotes;
using CityHelper.Services.MacroImages;
using CityHelper.Services.SlashCommands;
using CityHelper.ViewModels;

namespace CityHelper.Views;

public partial class ThemeOptionsView : UserControl
{
    public ThemeOptionsView()
    {
        InitializeComponent();
    }

    private async void OnSyncMacroImages(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;
        await MacroImageUi.SyncAsync(owner);
        (DataContext as ThemeOptionsViewModel)?.RefreshMacroImageStatus();
    }

    private async void OnReparseEmotes(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;
        await EmoteCache.ReparseWithDialogsAsync(owner);
        (DataContext as ThemeOptionsViewModel)?.RefreshEmoteStatus();
    }

    private async void OnUpdateBadges(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ThemeOptionsViewModel vm)
            await vm.UpdateBadgesAsync();
    }

    private async void OnReparseSlashCommands(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;
        await SlashCommandUi.ReparseAsync(owner);
        (DataContext as ThemeOptionsViewModel)?.RefreshSlashCommandStatus();
    }
}
