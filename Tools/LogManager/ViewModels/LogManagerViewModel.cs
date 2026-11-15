using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CityHelper.Services;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.LogManager.ViewModels;

public sealed partial class LogFileViewModel : ObservableObject
{
    public LogFileViewModel(FileInfo file)
    {
        Path = file.FullName;
        Name = file.Name;
        SizeText = FormatSize(file.Length);
        ModifiedText = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
        Date = LogOperations.DateOf(file.FullName);
        IsDaily = LogOperations.IsDailyLog(file.Name);
        IsInUse = LogOperations.IsOpenElsewhere(file.FullName);
    }

    public string Path { get; }
    public string Name { get; }

    /// <summary>The date in the file name (the file's date if it has none); the list is ordered by it.</summary>
    public DateTime Date { get; }

    /// <summary>A single day's log as written by the game (not a merged file).</summary>
    public bool IsDaily { get; }

    /// <summary>The game has the file open right now, so merging and cleanup leave it alone.</summary>
    public bool IsInUse { get; }

    public string DisplayName => IsInUse ? Name + "   (open in the game)" : Name;
    public string SizeText { get; }
    public string ModifiedText { get; }

    [ObservableProperty]
    private bool _isChecked;

    private static string FormatSize(long bytes) =>
        bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MB" :
        bytes >= 1 << 10 ? $"{bytes / (double)(1 << 10):0.#} KB" : $"{bytes} B";
}

/// <summary>Lists the selected account's log files and merges / trims the checked ones.</summary>
public sealed partial class LogManagerViewModel : ViewModelBase
{
    private readonly AppState _state;
    private int _version;

    public LogManagerViewModel(AppState state)
    {
        _state = state;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.SelectedInstallation) or nameof(AppState.SelectedAccount))
                Refresh(keepChecks: false);
        };
        Refresh(keepChecks: false);
    }

    public ObservableCollection<LogFileViewModel> Files { get; } = new();

    public IReadOnlyList<MergeType> MergeTypes { get; } = Enum.GetValues<MergeType>();

    [ObservableProperty]
    private MergeType _selectedMergeType = MergeType.Selected;

    [ObservableProperty]
    private string _folderText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand), nameof(CleanupCommand))]
    private bool _isBusy;

    private string? LogDirectory =>
        _state.SelectedInstallation is { } inst && _state.SelectedAccount is { } account
            ? Path.Combine(inst.Path, "accounts", account.Name, "logs")
            : null;

    /// <summary>Re-reads the folder. Checked files stay checked on a manual refresh.</summary>
    [RelayCommand]
    private void Refresh() => Refresh(keepChecks: true);

    private void Refresh(bool keepChecks)
    {
        _version++;
        var previouslyChecked = keepChecks
            ? Files.Where(f => f.IsChecked).Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>();
        Files.Clear();

        var dir = LogDirectory;
        if (dir is null)
        {
            FolderText = "";
            StatusText = "Select an installation and account.";
            return;
        }

        FolderText = dir;
        if (!Directory.Exists(dir))
        {
            StatusText = "This account has no logs folder.";
            return;
        }

        try
        {
            // Only the .txt logs directly in the folder (private chat and the Archive subfolder are separate)
            // In sequence by the date in the file name, newest at the top, so merged files sit among the days they cover
            var infos = new DirectoryInfo(dir).EnumerateFiles("*.txt", SearchOption.TopDirectoryOnly)
                .Where(f => f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                .Select(f => new LogFileViewModel(f) { IsChecked = previouslyChecked.Contains(f.Name) })
                .OrderByDescending(f => f.Date).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var file in infos)
                Files.Add(file);
            StatusText = Files.Count == 0 ? "No log files found." : $"{Files.Count} log file(s).";

            // A new list while Weekly / Monthly is chosen: select the days again (checks kept on a manual refresh)
            if (!keepChecks && SelectedMergeType != MergeType.Selected)
                AutoSelectDailies(setStatus: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not read the logs folder: " + ex.Message;
        }
    }

    partial void OnSelectedMergeTypeChanged(MergeType value)
    {
        if (value != MergeType.Selected)
            AutoSelectDailies(setStatus: true);
    }

    /// <summary>Weekly / Monthly merge the daily logs: check exactly those, except any the game has open.</summary>
    private void AutoSelectDailies(bool setStatus)
    {
        int open = 0, selected = 0;
        foreach (var f in Files)
        {
            f.IsChecked = f.IsDaily && !f.IsInUse;
            if (f.IsChecked) selected++;
            if (f.IsDaily && f.IsInUse) open++;
        }
        if (setStatus)
            StatusText = $"Selected {selected} daily log(s)" + (open > 0 ? $"; {open} open in the game will be skipped." : ".");
    }

    private bool CanRun => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task MergeAsync()
    {
        var dir = LogDirectory;
        var checkedFiles = Files.Where(f => f.IsChecked).Select(f => f.Path).ToList();
        if (dir is null || checkedFiles.Count == 0)
        {
            StatusText = "Check the log files to merge first.";
            return;
        }
        if (SelectedMergeType == MergeType.Selected && checkedFiles.Count < 2)
        {
            StatusText = "Check at least two files to merge.";
            return;
        }

        var settings = LogManagerSettings.Load();
        var type = SelectedMergeType;
        IsBusy = true;
        StatusText = "Merging...";
        var result = await Task.Run(() => LogOperations.Merge(dir, checkedFiles, type, settings.BackupMergedFiles));
        IsBusy = false;

        Refresh(keepChecks: false);
        StatusText = Summarize(result);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CleanupAsync()
    {
        var dir = LogDirectory;
        var checkedFiles = Files.Where(f => f.IsChecked).Select(f => f.Path).ToList();
        if (dir is null || checkedFiles.Count == 0)
        {
            StatusText = "Check the log files to clean up first.";
            return;
        }

        var settings = LogManagerSettings.Load();
        IsBusy = true;
        StatusText = "Cleaning up...";
        var result = await Task.Run(() =>
            LogOperations.Cleanup(dir, checkedFiles, settings.IncludedChannels, settings.BackupMergedFiles));
        IsBusy = false;

        Refresh(keepChecks: true);
        StatusText = Summarize(result);
    }

    private static string Summarize(LogOperationResult r)
    {
        var parts = new List<string>(r.Messages);
        if (r.Errors.Count > 0)
            parts.Add("Errors: " + string.Join("; ", r.Errors));
        return string.Join("   |   ", parts);
    }
}
