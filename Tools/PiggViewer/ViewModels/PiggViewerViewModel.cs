using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CityHelper.Services;
using CityHelper.Tools.PiggViewer.Core;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.PiggViewer.ViewModels;

public sealed partial class DirectoryNodeViewModel : ObservableObject
{
    public DirectoryNodeViewModel(DirectoryNode node, string? displayName = null)
    {
        Node = node;
        Name = displayName ?? node.Name;
        foreach (var child in node.Directories)
            Children.Add(new DirectoryNodeViewModel(child));
    }

    public DirectoryNode Node { get; }
    public string Name { get; }
    public ObservableCollection<DirectoryNodeViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;
}

public sealed class FileRowViewModel
{
    public FileRowViewModel(FileNode node)
    {
        Node = node;
        TypeText = FileTypes.Describe(node.Name);
        ModifiedText = node.ModifiedLocal.ToString("yyyy-MM-dd HH:mm");
        SizeText = FileTypes.FormatSize(node.Size);
        PackedText = node.Packed ? FileTypes.FormatSize(node.StoredSize) : "-";
    }

    public FileNode Node { get; }
    public string Name => Node.Name;
    public string TypeText { get; }
    public string ModifiedText { get; }
    public string SizeText { get; }
    public string PackedText { get; }
}

public static class FileTypes
{
    public static string Describe(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".ogg" => "Sound",
        ".texture" => "Texture",
        ".txt" => "Text",
        ".bin" => "Binary",
        ".vp" => "Vertex Shader",
        ".ttf" => "True Type Font",
        ".ttc" => "True Type Collection",
        _ => "Unknown",
    };

    public static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.00} GB" :
        bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB" :
        bytes >= 1L << 10 ? $"{bytes / (double)(1L << 10):0.#} KB" : $"{bytes} B";
}

/// <summary>
/// Browses .pigg archives (one archive, or all in a folder): folder tree, file list and a preview of the selected
/// file (texture / text / hex), with export and creating archives from a folder. Port of the Piglet viewer.
/// </summary>
public sealed partial class PiggViewerViewModel : ViewModelBase
{
    private const int TextPreviewLimit = 1 << 18;     // characters
    private const int HexPreviewLimit = 64 * 1024;    // bytes

    private readonly AppState _state;
    private readonly PiggVfs _vfs = new();
    private readonly PiggViewerSettings _settings = PiggViewerSettings.Load();
    private int _previewVersion;

    public PiggViewerViewModel(AppState state)
    {
        _state = state;
        StatusText = "Open an archive or a folder of archives.";
    }

    public ObservableCollection<DirectoryNodeViewModel> Roots { get; } = new();
    public ObservableCollection<FileRowViewModel> Files { get; } = new();

    [ObservableProperty] private DirectoryNodeViewModel? _selectedDirectory;
    [ObservableProperty] private PreviewViewModel? _preview;
    [ObservableProperty] private string _mountText = "";
    [ObservableProperty] private string _statusText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenArchiveCommand), nameof(OpenFolderCommand), nameof(OpenAssetsCommand),
        nameof(OpenModsCommand), nameof(NewFromFolderCommand), nameof(PackTextureCommand))]
    private bool _isBusy;

    private IReadOnlyList<FileRowViewModel> _selectedFiles = Array.Empty<FileRowViewModel>();

    /// <summary>Set by the view when the list's selection changes (a multi-select list has no bindable selection).</summary>
    public void SetSelectedFiles(IReadOnlyList<FileRowViewModel> files)
    {
        _selectedFiles = files;
        UpdateStatus();
        if (files.Count == 1)
            _ = ShowPreviewAsync(files[0]);
        else
        {
            _previewVersion++;
            Preview = null;
        }
    }

    partial void OnSelectedDirectoryChanged(DirectoryNodeViewModel? value)
    {
        Files.Clear();
        Preview = null;
        _previewVersion++;
        _selectedFiles = Array.Empty<FileRowViewModel>();
        if (value != null)
            foreach (var file in value.Node.Files)
                Files.Add(new FileRowViewModel(file));
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_vfs.Root is null)
            return;
        StatusText = _selectedFiles.Count > 0
            ? $"{_selectedFiles.Count} item(s) selected, {FileTypes.FormatSize(_selectedFiles.Sum(f => (long)f.Node.Size))}"
            : $"{Files.Count} item(s)";
    }

    // ------------------------------------------------------------ opening

    private bool CanOpen => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenArchiveAsync()
    {
        var path = await PiggDialogs.OpenArchiveAsync("Open Archive", _settings.LastArchiveFolder);
        if (path is null)
            return;
        _settings.LastArchiveFolder = Path.GetDirectoryName(path) ?? "";
        _settings.Save();
        await MountAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenFolderAsync()
    {
        var path = await PiggDialogs.PickFolderAsync("Open Folder of Archives", _settings.LastOpenFolder);
        if (path is null)
            return;
        _settings.LastOpenFolder = path;
        _settings.Save();
        await MountAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenAssetsAsync() => MountInstallationFolderAsync(Path.Combine("assets", GameEnvironment()));

    /// <summary>The settings folder name this installation uses (live, beta...), as set in Config Sync; the game's archives sit in assets\&lt;env&gt;.</summary>
    private string GameEnvironment()
    {
        if (_state.SelectedInstallation is not { } inst)
            return CityHelper.Tools.ConfigSync.SyncItems.DefaultGameEnvironment;
        var names = _state.Installations.Select(i => i.Name).ToList();
        var settings = CityHelper.Tools.ConfigSync.ConfigSyncSettings.Load(names, _state.Installations.FirstOrDefault(i => i.IsDefault)?.Name);
        return settings.Find(inst.Name)?.GameEnvironment ?? CityHelper.Tools.ConfigSync.SyncItems.DefaultGameEnvironment;
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenModsAsync() => MountInstallationFolderAsync(Path.Combine("assets", "mods"));

    private async Task MountInstallationFolderAsync(string relative)
    {
        if (_state.SelectedInstallation is not { } inst)
        {
            await PiggDialogs.ShowMessageAsync("PIGG Viewer", "Select an installation first.");
            return;
        }
        var path = Path.Combine(inst.Path, relative);
        if (!Directory.Exists(path))
        {
            await PiggDialogs.ShowMessageAsync("PIGG Viewer", $"This installation has no folder:\n{path}");
            return;
        }
        await MountAsync(path);
    }

    private async Task MountAsync(string path)
    {
        IsBusy = true;
        StatusText = "Opening...";
        string? error = null;
        try
        {
            await Task.Run(() => _vfs.Mount(path));
        }
        catch (Exception ex) when (ex is PiggException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
        }
        IsBusy = false;

        if (error != null)
        {
            StatusText = "Could not open: " + error;
            await PiggDialogs.ShowMessageAsync("Open Archive", $"{path}\n\n{error}");
            return;
        }

        ShowMount();
    }

    private void ShowMount()
    {
        Roots.Clear();
        SelectedDirectory = null;
        if (_vfs.Root is not { } root)
        {
            MountText = "";
            return;
        }

        var rootVm = new DirectoryNodeViewModel(root, root.Name) { IsExpanded = true };
        Roots.Add(rootVm);
        MountText = _vfs.MountType == MountType.Folder
            ? $"[ {root.Name} ] - {_vfs.ArchiveCount} archive(s)"
            : root.Name;
        SelectedDirectory = rootVm;
    }

    [RelayCommand]
    private void Close()
    {
        _previewVersion++;
        Preview = null;
        Files.Clear();
        Roots.Clear();
        SelectedDirectory = null;
        _vfs.Unmount();
        MountText = "";
        StatusText = "Open an archive or a folder of archives.";
    }

    // ------------------------------------------------------------ preview

    private sealed record PreviewData(byte[] Blob, TextureData? Texture, string? Problem);

    private async Task ShowPreviewAsync(FileRowViewModel row)
    {
        int version = ++_previewVersion;
        Preview = null;
        var node = row.Node;
        var extension = Path.GetExtension(node.Name).ToLowerInvariant();

        PreviewData? data = null;
        string? error = null;
        try
        {
            data = await Task.Run(() =>
            {
                var blob = node.Load();
                if (extension != ".texture")
                    return new PreviewData(blob, null, null);
                var texture = TextureReader.Read(blob, out var problem);
                return new PreviewData(blob, texture, problem);
            });
        }
        catch (Exception ex) when (ex is PiggException or IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
        {
            error = ex.Message;
        }
        if (version != _previewVersion)
            return;                                   // another file was picked while this one loaded

        if (data is null)
        {
            Preview = new MessagePreviewViewModel($"Could not read {node.Name}: {error}", () => Task.CompletedTask);
            return;
        }

        var blobExport = new Func<Task>(() => ExportBytesAsync(node.Name, data.Blob, new[] { new ExportSegment(node.Name, 0, data.Blob.Length) }));

        if (extension == ".texture")
        {
            Preview = data.Texture is { } texture
                ? new TexturePreviewViewModel(texture, () => ExportBytesAsync(node.Name, data.Blob, texture.Segments))
                : new MessagePreviewViewModel($"This texture can't be shown: {data.Problem}", blobExport);
        }
        else if (extension is ".txt" or ".vp")
        {
            var text = Encoding.UTF8.GetString(data.Blob);
            var truncated = text.Length > TextPreviewLimit;
            Preview = new TextPreviewViewModel(truncated ? text[..TextPreviewLimit] : text,
                truncated ? $"Showing the first {TextPreviewLimit:N0} characters." : "", blobExport);
        }
        else
        {
            Preview = new BinaryPreviewViewModel(HexDump(data.Blob),
                data.Blob.Length > HexPreviewLimit ? $"Showing the first {HexPreviewLimit:N0} of {data.Blob.Length:N0} bytes." : "",
                blobExport);
        }
    }

    private static string HexDump(byte[] data)
    {
        var sb = new StringBuilder();
        int length = Math.Min(data.Length, HexPreviewLimit);
        for (int i = 0; i < length; i += 16)
        {
            sb.Append(i.ToString("X8")).Append("  ");
            for (int j = 0; j < 16; j++)
            {
                sb.Append(i + j < length ? data[i + j].ToString("X2") : "  ").Append(j == 7 ? "  " : " ");
            }
            sb.Append(' ');
            for (int j = 0; j < 16 && i + j < length; j++)
            {
                var c = data[i + j];
                sb.Append(c is >= 32 and < 127 ? (char)c : '.');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------ export

    private async Task ExportBytesAsync(string name, byte[] contents, IEnumerable<ExportSegment> segments)
    {
        var folder = await PickExportFolderAsync();
        if (folder is null)
            return;
        var list = segments.ToList();
        await RunExportAsync(() => PiggVfs.ExportSegments(contents, list, folder),
            list.Count == 1 ? $"Exported {list[0].Name}" : $"Exported {name} as {list.Count} files");
    }

    [RelayCommand]
    private async Task ExportFilesAsync()
    {
        if (_selectedFiles.Count == 0)
        {
            await PiggDialogs.ShowMessageAsync("Export", "Select the files to export first.");
            return;
        }
        var folder = await PickExportFolderAsync();
        if (folder is null)
            return;
        var nodes = _selectedFiles.Select(f => f.Node).ToList();
        await RunExportAsync(() => PiggVfs.ExportFiles(nodes, folder), $"Exported {nodes.Count} file(s)");
    }

    [RelayCommand]
    private async Task ExportDirectoryAsync()
    {
        if (SelectedDirectory is not { } directory)
        {
            await PiggDialogs.ShowMessageAsync("Export", "Select a folder in the tree first.");
            return;
        }
        var folder = await PickExportFolderAsync();
        if (folder is null)
            return;
        await RunExportAsync(() => PiggVfs.ExportDirectory(directory.Node, folder), $"Exported folder '{directory.Name}'");
    }

    private async Task<string?> PickExportFolderAsync()
    {
        var folder = await PiggDialogs.PickFolderAsync("Export to Folder", _settings.LastExportFolder);
        if (folder != null)
        {
            _settings.LastExportFolder = folder;
            _settings.Save();
        }
        return folder;
    }

    private async Task RunExportAsync(Action work, string doneText)
    {
        IsBusy = true;
        StatusText = "Exporting...";
        string? error = null;
        try
        {
            await Task.Run(work);
        }
        catch (Exception ex) when (ex is PiggException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            error = ex.Message;
        }
        IsBusy = false;

        if (error != null)
        {
            StatusText = "Export failed: " + error;
            await PiggDialogs.ShowMessageAsync("Export", error);
            return;
        }
        StatusText = doneText;
    }

    // ------------------------------------------------------------ creating

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task NewFromFolderAsync()
    {
        var source = await PiggDialogs.PickFolderAsync("Folder to Pack into an Archive", _settings.LastNewArchiveSource);
        if (source is null)
            return;
        _settings.LastNewArchiveSource = Path.GetDirectoryName(source.TrimEnd(Path.DirectorySeparatorChar)) ?? source;

        var suggested = new DirectoryInfo(source).Name + ".pigg";
        var target = await PiggDialogs.SaveArchiveAsync("Save New Archive", _settings.LastNewArchiveFolder, suggested);
        if (target is null)
            return;
        _settings.LastNewArchiveFolder = Path.GetDirectoryName(target) ?? "";
        _settings.Save();

        IsBusy = true;
        string? error = null;
        var progress = new Progress<string>(text => StatusText = "Packing " + text);
        try
        {
            await Task.Run(() => PiggVfs.CreateArchive(source, target, progress));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
        }
        IsBusy = false;

        if (error != null)
        {
            StatusText = "Could not create the archive: " + error;
            await PiggDialogs.ShowMessageAsync("New Archive", error);
            return;
        }
        StatusText = $"Created {Path.GetFileName(target)}";
    }

    // ------------------------------------------------------------ texture packing

    /// <summary>
    /// Builds a .texture from an edited .dds plus the .hdr (and .mip) that were exported with it, which sit next to the
    /// image under the same name. The mip block is regenerated from the edited image.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task PackTextureAsync()
    {
        var imagePath = await PiggDialogs.OpenImageAsync("Edited Texture Image (.dds)", _settings.LastTextureFolder);
        if (imagePath is null)
            return;

        var folder = Path.GetDirectoryName(imagePath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(imagePath);
        var headerPath = Path.Combine(folder, stem + ".hdr");
        var mipPath = Path.Combine(folder, stem + ".mip");
        if (!File.Exists(headerPath))
        {
            await PiggDialogs.ShowMessageAsync("Pack Texture",
                $"{stem}.hdr was not found next to the image.\n\nExport the original texture first (it is saved as {stem}.hdr, {stem}.dds and, for most textures, {stem}.mip), edit the .dds, then pack it.");
            return;
        }

        var target = await PiggDialogs.SaveTextureAsync("Save Texture", folder, stem + ".texture");
        if (target is null)
            return;
        _settings.LastTextureFolder = Path.GetDirectoryName(target) ?? folder;
        _settings.Save();

        IsBusy = true;
        string? error = null;
        try
        {
            await Task.Run(() =>
            {
                var header = File.ReadAllBytes(headerPath);
                var mip = File.Exists(mipPath) ? File.ReadAllBytes(mipPath) : null;
                var image = File.ReadAllBytes(imagePath);
                File.WriteAllBytes(target, TexturePacker.Pack(header, mip, image));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            error = ex.Message;
        }
        IsBusy = false;

        if (error != null)
        {
            StatusText = "Could not pack the texture: " + error;
            await PiggDialogs.ShowMessageAsync("Pack Texture", error);
            return;
        }
        StatusText = $"Created {Path.GetFileName(target)}";
    }
}
