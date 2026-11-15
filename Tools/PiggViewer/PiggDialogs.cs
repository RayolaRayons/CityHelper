using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CityHelper.Views;

namespace CityHelper.Tools.PiggViewer;

/// <summary>File / folder pickers and message boxes over the main window.</summary>
public static class PiggDialogs
{
    private static readonly FilePickerFileType PiggFiles = new("Pigg archives") { Patterns = new[] { "*.pigg" } };

    private static Window? Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static async Task<string?> OpenArchiveAsync(string title, string? startFolder)
    {
        if (Owner is not { } owner) return null;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { PiggFiles, FilePickerFileTypes.All },
            SuggestedStartLocation = await FolderAsync(owner, startFolder),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> OpenImageAsync(string title, string? startFolder)
    {
        if (Owner is not { } owner) return null;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("DDS images") { Patterns = new[] { "*.dds" } }, FilePickerFileTypes.All },
            SuggestedStartLocation = await FolderAsync(owner, startFolder),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> SaveTextureAsync(string title, string? startFolder, string suggestedName)
    {
        if (Owner is not { } owner) return null;
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "texture",
            FileTypeChoices = new[] { new FilePickerFileType("Texture files") { Patterns = new[] { "*.texture" } } },
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await FolderAsync(owner, startFolder),
        });
        return file?.TryGetLocalPath();
    }

    public static async Task<string?> SaveArchiveAsync(string title, string? startFolder, string suggestedName)
    {
        if (Owner is not { } owner) return null;
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "pigg",
            FileTypeChoices = new[] { PiggFiles, FilePickerFileTypes.All },
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await FolderAsync(owner, startFolder),
        });
        return file?.TryGetLocalPath();
    }

    public static async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        if (Owner is not { } owner) return null;
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(owner, startFolder),
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public static async Task ShowMessageAsync(string title, string message)
    {
        if (Owner is { } owner)
            await new MessageBox(title, message).ShowDialog(owner);
    }

    private static async Task<IStorageFolder?> FolderAsync(Window owner, string? path) =>
        string.IsNullOrEmpty(path) || !Directory.Exists(path) ? null : await owner.StorageProvider.TryGetFolderFromPathAsync(path);
}
