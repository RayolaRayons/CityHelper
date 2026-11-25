using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CityHelper.Tools.CommandLookup.ViewModels;

namespace CityHelper.Tools.CommandLookup.Views;

public partial class CommandLookupView : UserControl
{
    private CommandLookupViewModel? _vm;

    public CommandLookupView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
                _vm.PropertyChanged -= OnViewModelChanged;
            _vm = DataContext as CommandLookupViewModel;
            if (_vm != null)
                _vm.PropertyChanged += OnViewModelChanged;
            UpdateAccordion();
        };
    }

    private CommandRow? Row => (DataContext as CommandLookupViewModel)?.Selected;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CommandLookupViewModel.IsCommandsOpen) or nameof(CommandLookupViewModel.IsEmotesOpen))
            UpdateAccordion();
    }

    /// <summary>The open section's list takes all the room left; the closed one takes none.</summary>
    private void UpdateAccordion()
    {
        if (_vm is null)
            return;
        var rows = Accordion.RowDefinitions;
        rows[1].Height = _vm.IsCommandsOpen ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        rows[3].Height = _vm.IsEmotesOpen ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    private void OnToggleCommands(object? sender, RoutedEventArgs e) => _vm?.ToggleCommandsCommand.Execute(null);

    private void OnToggleEmotes(object? sender, RoutedEventArgs e) => _vm?.ToggleEmotesCommand.Execute(null);

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Row is { } row && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(row.SyntaxLine);
    }

    private async void OnCopyEmote(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedEmote is { } emote && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(emote.UseLine);
    }

    private void OnOpenWiki(object? sender, RoutedEventArgs e)
    {
        if (Row is { } row && TopLevel.GetTopLevel(this) is { } top && Uri.TryCreate(row.WikiUrl, UriKind.Absolute, out var uri))
            _ = top.Launcher.LaunchUriAsync(uri);
    }
}
