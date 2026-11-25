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
    public DirectoryNodeViewModel(DirectoryNode node, string? displayName = null, DirectoryNodeViewModel? parent = null)
    {
        Node = node;
        Parent = parent;
        Name = displayName ?? node.Name;
        foreach (var child in node.Directories)
            Children.Add(new DirectoryNodeViewModel(child, null, this));
    }

    public DirectoryNode Node { get; }
    public DirectoryNodeViewModel? Parent { get; }
    public string Name { get; }
    public ObservableCollection<DirectoryNodeViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;
}

/// <summary>A folder under the installation's assets folder that holds archives, or one archive in it.</summary>
public sealed record PiggChoice(string Name, string Path, bool IsAll = false)
{
    public override string ToString() => Name;
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
public sealed partial class PiggViewerViewModel : ViewModelBase, IStatusSource
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
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppState.SelectedInstallation))
                RefreshFolders();
        };
        RefreshFolders();
    }

    // ------------------------------------------------------------ toolbar: folder and archive pickers

    public const string AllFoldersName = "(All folders under assets)";

    /// <summary>Folders of the selected installation's assets folder that have archives at their top level (live, mods...), after an "all folders" entry.</summary>
    public ObservableCollection<PiggChoice> Folders { get; } = new();

    /// <summary>The archives at the top level of the chosen folder.</summary>
    public ObservableCollection<PiggChoice> Archives { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadFolderCommand))]
    private PiggChoice? _selectedFolder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadArchiveCommand))]
    private PiggChoice? _selectedArchive;

    private void RefreshFolders()
    {
        var previous = SelectedFolder?.Path;
        Folders.Clear();
        if (_state.SelectedInstallation is { } inst)
        {
            var assets = Path.Combine(inst.Path, "assets");
            try
            {
                if (Directory.Exists(assets))
                    foreach (var dir in Directory.EnumerateDirectories(assets).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                    {
                        var name = Path.GetFileName(dir);
                        bool hasArchives = Directory.EnumerateFiles(dir, "*.pigg", SearchOption.TopDirectoryOnly).Any();
                        if (hasArchives || name.Equals("mods", StringComparison.OrdinalIgnoreCase))
                            Folders.Add(new PiggChoice(name, dir));
                    }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // an unreadable assets folder just offers nothing
            }
        }
        // Everything under assets at once
        if (Folders.Count > 0 && _state.SelectedInstallation is { } installation)
            Folders.Insert(0, new PiggChoice(AllFoldersName, Path.Combine(installation.Path, "assets"), IsAll: true));

        SelectedFolder = Folders.FirstOrDefault(f => f.Path == previous)
                         ?? Folders.FirstOrDefault(f => !f.IsAll && f.Name.Equals(GameEnvironment(), StringComparison.OrdinalIgnoreCase))
                         ?? Folders.FirstOrDefault(f => !f.IsAll)
                         ?? Folders.FirstOrDefault();
    }

    partial void OnSelectedFolderChanged(PiggChoice? value)
    {
        Archives.Clear();
        SelectedArchive = null;
        if (value is null || value.IsAll)
            return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(value.Path, "*.pigg", SearchOption.TopDirectoryOnly)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                Archives.Add(new PiggChoice(Path.GetFileName(file), file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not read the folder: " + ex.Message;
        }
    }

    private bool CanLoadFolder => !IsBusy && SelectedFolder != null;
    private bool CanLoadArchive => !IsBusy && SelectedArchive != null;

    [RelayCommand(CanExecute = nameof(CanLoadFolder))]
    private Task LoadFolderAsync()
    {
        var folder = SelectedFolder!;
        if (!folder.IsAll)
            return MountAsync(folder.Path);

        // Every folder under assets together, each as a directory of its own name
        var all = Folders.Where(f => !f.IsAll).Select(f => (f.Name, f.Path)).ToList();
        return MountAsync(folder.Path, () => _vfs.MountAll(folder.Path, all));
    }

    [RelayCommand(CanExecute = nameof(CanLoadArchive))]
    private Task LoadArchiveAsync() => MountAsync(SelectedArchive!.Path);

    /// <summary>The label of the one Export button: what it will export right now.</summary>
    public string ExportText =>
        _selectedFiles.Count > 0 ? $"Export Selected ({_selectedFiles.Count:N0})"
        : SelectedDirectory != null ? "Export Folder"
        : "Export";

    private bool CanExport => !IsBusy && (_selectedFiles.Count > 0 || SelectedDirectory != null);

    /// <summary>
    /// Exports the selection: the files selected in the list, or, when none are, the selected folder with everything
    /// under it (the top folder exports everything that is loaded).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportAsync() => _selectedFiles.Count > 0 ? ExportFilesAsync() : ExportDirectoryAsync();

    private void RefreshExport()
    {
        OnPropertyChanged(nameof(ExportText));
        ExportCommand.NotifyCanExecuteChanged();
    }

    public ObservableCollection<DirectoryNodeViewModel> Roots { get; } = new();
    public ObservableCollection<FileRowViewModel> Files { get; } = new();

    [ObservableProperty] private DirectoryNodeViewModel? _selectedDirectory;
    [ObservableProperty] private PreviewViewModel? _preview;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBarText))]
    private string _mountText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBarText))]
    private string _statusText = "";

    /// <summary>What is open and what is selected; shown at the right of the main status bar.</summary>
    public string StatusBarText => string.Join("   |   ", new[] { ResultsText, MountText, StatusText }.Where(t => t.Length > 0));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenArchiveCommand), nameof(OpenFolderCommand), nameof(OpenAssetsCommand),
        nameof(OpenModsCommand), nameof(NewFromFolderCommand), nameof(PackTextureCommand),
        nameof(LoadFolderCommand), nameof(LoadArchiveCommand), nameof(ExportCommand))]
    private bool _isBusy;

    private IReadOnlyList<FileRowViewModel> _selectedFiles = Array.Empty<FileRowViewModel>();

    /// <summary>Set by the view when the list's selection changes (a multi-select list has no bindable selection).</summary>
    public void SetSelectedFiles(IReadOnlyList<FileRowViewModel> files)
    {
        _selectedFiles = files;
        RefreshExport();
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
        RefreshExport();
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

    private async Task MountAsync(string path, Action? mount = null)
    {
        IsBusy = true;
        StatusText = "Opening...";
        string? error = null;
        try
        {
            await Task.Run(mount ?? (() => _vfs.Mount(path)));
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
        RefreshExport();
        if (_vfs.Root is not { } root)
        {
            MountText = "";
            return;
        }

        var rootVm = new DirectoryNodeViewModel(root, root.Name) { IsExpanded = true };
        Roots.Add(rootVm);
        _directoryViewModels.Clear();
        IndexDirectories(rootVm);
        MountText = _vfs.MountType == MountType.Folder
            ? $"[ {root.Name} ] - {_vfs.ArchiveCount} archive(s)"
            : root.Name;
        SelectedDirectory = rootVm;
    }

    private void IndexDirectories(DirectoryNodeViewModel vm)
    {
        _directoryViewModels[vm.Node] = vm;
        foreach (var child in vm.Children)
            IndexDirectories(child);
    }

    [RelayCommand]
    private void Close()
    {
        _directoryViewModels.Clear();
        _previewVersion++;
        Preview = null;
        Files.Clear();
        Roots.Clear();
        SelectedDirectory = null;
        _vfs.Unmount();
        RefreshExport();
        MountText = "";
        StatusText = "Open an archive or a folder of archives.";
        RunSearch();
    }

    // ------------------------------------------------------------ search

    private readonly Dictionary<DirectoryNode, DirectoryNodeViewModel> _directoryViewModels = new();
    private List<(DirectoryNode Directory, FileNode? File)> _results = new();
    private int _currentResult = -1;

    public const string FilterAll = "All";
    public const string FilterFolder = "Folder";
    public const string FilterTexture = "Texture";
    public const string FilterSound = "Sound (ogg)";
    public const string FilterPopmenu = "Popmenu";
    public const string FilterBinary = "Binary";

    /// <summary>What the search looks at: everything, only folder names, or only files of one type.</summary>
    public IReadOnlyList<string> SearchFilters { get; } =
        new[] { FilterAll, FilterFolder, FilterTexture, FilterSound, FilterPopmenu, FilterBinary };

    [ObservableProperty] private string _selectedSearchFilter = FilterAll;

    partial void OnSelectedSearchFilterChanged(string value)
    {
        _currentResult = -1;
        RunSearch();
    }

    /// <summary>The file extension a type filter stands for; null for All and Folder.</summary>
    private static string? ExtensionFor(string filter) => filter switch
    {
        FilterTexture => ".texture",
        FilterSound => ".ogg",
        FilterPopmenu => ".mnu",
        FilterBinary => ".bin",
        _ => null,
    };

    /// <summary>Text to look for in the folder and texture (file) names of what is loaded.</summary>
    [ObservableProperty] private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBarText))]
    private string _resultsText = "";

    /// <summary>Raised to scroll the folder tree to a result's folder / select a result's file in the list.</summary>
    public event Action<DirectoryNodeViewModel>? DirectoryRequested;
    public event Action<FileRowViewModel>? FileRequested;

    partial void OnSearchTextChanged(string value)
    {
        _currentResult = -1;
        RunSearch();
    }

    private bool HasResults => _results.Count > 0;

    /// <summary>All folders and files whose name contains the text, in tree order (a folder before its files).</summary>
    private void RunSearch()
    {
        _results = new List<(DirectoryNode, FileNode?)>();
        var text = SearchText.Trim();
        if (text.Length > 0 && _vfs.Root is { } root)
            Collect(root, text, isRoot: true);
        if (_currentResult >= _results.Count)
            _currentResult = -1;
        UpdateResultsText(text);
        NextResultCommand.NotifyCanExecuteChanged();
        PreviousResultCommand.NotifyCanExecuteChanged();
    }

    private void Collect(DirectoryNode directory, string text, bool isRoot)
    {
        var filter = SelectedSearchFilter;
        var extension = ExtensionFor(filter);

        // Folder names count for All and Folder; files for All and for their own type
        if (!isRoot && (filter is FilterAll or FilterFolder) && directory.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            _results.Add((directory, null));
        if (filter != FilterFolder)
            foreach (var file in directory.Files)
                if (file.Name.Contains(text, StringComparison.OrdinalIgnoreCase) &&
                    (extension is null || Path.GetExtension(file.Name).Equals(extension, StringComparison.OrdinalIgnoreCase)))
                    _results.Add((directory, file));
        foreach (var child in directory.Directories)
            Collect(child, text, isRoot: false);
    }

    private void UpdateResultsText(string text)
    {
        ResultsText = text.Length == 0 ? ""
                    : _results.Count == 0 ? "No matches"
                    : _currentResult >= 0 ? $"Result {_currentResult + 1:N0} of {_results.Count:N0}"
                    : _results.Count == 1 ? "1 match" : $"{_results.Count:N0} matches";
    }

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void NextResult() => StepResult(+1);

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void PreviousResult() => StepResult(-1);

    private void StepResult(int direction)
    {
        if (_results.Count == 0)
            return;
        _currentResult = _currentResult < 0
            ? (direction > 0 ? 0 : _results.Count - 1)
            : (_currentResult + direction + _results.Count) % _results.Count;
        UpdateResultsText(SearchText.Trim());

        var (directory, file) = _results[_currentResult];
        if (!_directoryViewModels.TryGetValue(directory, out var vm))
            return;
        for (var parent = vm.Parent; parent != null; parent = parent.Parent)
            parent.IsExpanded = true;                   // show the folder in the tree
        SelectedDirectory = vm;                         // fills the file list
        DirectoryRequested?.Invoke(vm);
        if (file != null && Files.FirstOrDefault(f => f.Node == file) is { } row)
            FileRequested?.Invoke(row);
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
