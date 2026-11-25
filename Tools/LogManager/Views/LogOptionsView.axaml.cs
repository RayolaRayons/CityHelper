using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CityHelper.Tools.LogManager.ViewModels;

namespace CityHelper.Tools.LogManager.Views;

public partial class LogOptionsView : UserControl
{
    public LogOptionsView()
    {
        InitializeComponent();
    }

    private async void OnBrowseCopiesFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LogOptionsViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
            return;
        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(vm.CopiesFolder) && Directory.Exists(vm.CopiesFolder))
            start = await top.StorageProvider.TryGetFolderFromPathAsync(vm.CopiesFolder);
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the folder for copies of changed logs",
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            vm.CopiesFolder = path;
    }
}
