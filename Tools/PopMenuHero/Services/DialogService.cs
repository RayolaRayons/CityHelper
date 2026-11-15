using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CityHelper.Tools.PopMenuHero.Views;

namespace CityHelper.Tools.PopMenuHero.Services;

/// <summary>IDialogService backed by whichever top level hosts the tool view (set when the view attaches).</summary>
public sealed class DialogService : IDialogService
{
    private static readonly FilePickerFileType MnuFiles = new("PopMenu Files") { Patterns = ["*.mnu"] };

    public TopLevel? Owner { get; set; }

    private TopLevel Top => Owner ?? throw new InvalidOperationException("Tool view is not attached to a window.");

    public Task<bool> ConfirmAsync(string title, string message) =>
        MessageDialog.ShowAsync((Window)Top, title, message, MessageKind.Info, yesNo: true);

    public Task ShowMessageAsync(string title, string message, MessageKind kind = MessageKind.Info) =>
        MessageDialog.ShowAsync((Window)Top, title, message, kind, yesNo: false);

    public async Task<string?> PickOpenFileAsync(string title, string? startDirectory)
    {
        var files = await Top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [MnuFiles, FilePickerFileTypes.All],
            SuggestedStartLocation = await FolderAsync(startDirectory),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string? startDirectory, string? suggestedFileName)
    {
        var file = await Top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "mnu",
            FileTypeChoices = [MnuFiles, FilePickerFileTypes.All],
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await FolderAsync(startDirectory),
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title, string? startDirectory)
    {
        var folders = await Top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(startDirectory),
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task<IStorageFolder?> FolderAsync(string? path) =>
        string.IsNullOrEmpty(path) || !Directory.Exists(path) ? null : await Top.StorageProvider.TryGetFolderFromPathAsync(path);

    public void OpenUrl(string url) => _ = Top.Launcher.LaunchUriAsync(new Uri(url));

    private MnuPreviewWindow? _previewWindow;

    public void ShowPreview(CityHelper.Tools.PopMenuHero.ViewModels.MnuPreviewViewModel preview)
    {
        if (_previewWindow is { } existing)
        {
            existing.Activate();
            return;
        }
        var window = new MnuPreviewWindow(preview);
        window.Closed += (_, _) => _previewWindow = null;
        _previewWindow = window;
        if (Owner is Window owner)
            window.Show(owner);   // stays above the main window, closes with it
        else
            window.Show();
    }

    public void CloseWindow() => (Owner as Window)?.Close();
}
