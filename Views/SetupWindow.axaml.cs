using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CityHelper.ViewModels;

namespace CityHelper.Views;

/// <summary>First-run setup. Closes when finished, skipped or dismissed; the app then continues to the main window.</summary>
public partial class SetupWindow : Window
{
    private bool _done;

    public SetupWindow()
    {
        InitializeComponent();
        DataContext = new SetupViewModel();
    }

    private SetupViewModel Model => (SetupViewModel)DataContext!;

    private async System.Threading.Tasks.Task<string?> PickFolderAsync(string title, string? startPath)
    {
        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startPath) && Directory.Exists(startPath))
            start = await StorageProvider.TryGetFolderFromPathAsync(startPath);
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync("Select City of Heroes folder", null) is { } path)
            Model.AddFolder(path);
    }

    private void OnSearchAgain(object? sender, RoutedEventArgs e)
    {
        foreach (var found in Services.InstallationDetector.Detect())
            Model.AddFolder(found.Path);
    }

    private async void OnBrowseSyncFolder(object? sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync("Select the sync folder", Model.SyncFolder) is { } path)
            Model.SyncFolder = path;
    }

    private void OnFinish(object? sender, RoutedEventArgs e)
    {
        if (!Model.Finish())
            return;
        _done = true;
        Close();
    }

    private void OnSkip(object? sender, RoutedEventArgs e)
    {
        Model.MarkCompleted();
        _done = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing the window counts as skipping, so setup isn't forced on every start
        if (!_done)
            Model.MarkCompleted();
        base.OnClosing(e);
    }
}
