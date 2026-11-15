using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CityHelper.Tools.ConfigSync.ViewModels;

namespace CityHelper.Tools.ConfigSync.Views;

public partial class ConfigSyncOptionsView : UserControl
{
    public ConfigSyncOptionsView()
    {
        InitializeComponent();
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if ((DataContext as ConfigSyncOptionsViewModel)?.SelectedEnvironment is not { } env ||
            TopLevel.GetTopLevel(this) is not { } top)
            return;

        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(env.BackupFolder) && System.IO.Directory.Exists(env.BackupFolder))
            start = await top.StorageProvider.TryGetFolderFromPathAsync(env.BackupFolder);

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select backup folder",
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            env.BackupFolder = path;
    }
}
